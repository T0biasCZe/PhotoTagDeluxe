using Microsoft.AspNetCore.Mvc;
using PhotoTag.Contracts;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System;
using System.Threading.Tasks;
using FluentEmail.Core;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Net.Mail;
using System.Net;
using System.Diagnostics;

namespace PhotoTag.Server.Controllers {
	[ApiController]
	[Route("api/[controller]")]
	public class UsersController : ControllerBase {
		private static readonly string _debugLogPath = Path.Combine(Path.GetTempPath(), "phototag_debug_register.log");

		private static void LogDebug(string message) {
			try {
				var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
				Debug.WriteLine($"[{timestamp}] {message}");
				Console.WriteLine($"[{timestamp}] {message}");
				System.IO.File.AppendAllText(_debugLogPath, $"[{timestamp}] {message}\n");
			} catch (Exception ex) {
				// Fallback - try to write error about the error
				try {
					System.IO.File.AppendAllText(Path.Combine(Path.GetTempPath(), "phototag_log_error.txt"), $"Failed to write debug log: {ex.Message}\n");
				} catch { }
			}
		}

		// In-memory pending registrations (verification codes)
		private class PendingRegistration {
			public string Username { get; set; }
			public string Email { get; set; }
			public string Code { get; set; }
			public DateTime CreatedAtUtc { get; set; }
			public DateTime ExpiresAtUtc { get; set; }
			public int Attempts { get; set; }
			public DateTime? BlockedUntilUtc { get; set; }
			public string ClientIp { get; set; }
		}

		// Minimum username length. Mirrored client-side so the user gets immediate feedback,
		// but enforced here because the client is not the only possible caller.
		public const int MinUsernameLength = 4;

		private static readonly List<PendingRegistration> _pendingRegistrations = new List<PendingRegistration>();
		private static readonly object _pendingLock = new object();
		private static readonly List<PendingRegistration> _pendingLogins = new List<PendingRegistration>();
		private static readonly object _pendingLoginLock = new object();
		private static string UserDataRoot => Program.UserDataPath;
		private static string UsersXmlPath => Path.Combine(UserDataRoot, "Users.xml");
		private static string JwtSecret => Program.JwtSecret;
		private static List<User> _usersCache;

		bool IsValidEmail(string email) {
			try {
				var addr = new MailAddress(email);
				return true;
			}
			catch {
				return false;
			}
		}

		public static void InitializeUsers() {
			var userDataDir = Path.GetDirectoryName(UsersXmlPath);
			if (!Directory.Exists(userDataDir)) Directory.CreateDirectory(userDataDir);
			if (!System.IO.File.Exists(UsersXmlPath)) {
				_usersCache = new List<User>();
			} else {
				var serializer = new XmlSerializer(typeof(List<User>));
				using (var stream = System.IO.File.OpenRead(UsersXmlPath)) {
					_usersCache = (List<User>)serializer.Deserialize(stream);
				}
			}
		}

		/*ServerInfo response:
		  200 OK + ServerInfoResponse. Anonymous; used by clients to discover whether an email
		  address is required before the user submits the registration form, and to confirm the
		  entered URL actually points at a PhotoTag server.
		*/
		[HttpGet("serverinfo")]
		public IActionResult ServerInfo() {
			return Ok(new ServerInfoResponse {
				Product = "PhotoTag",
				ApiVersion = 1,
				MandatoryEmail = Program.ServerDemandsEmail,
				EmailVerification = Program.EmailVerificationWanted,
				MinUsernameLength = MinUsernameLength,
				MaxThumbnailWidth = Program.MaxThumbnailWidth,
				MaxThumbnailSizeKB = Program.MaxThumbnailSizeKB,
				VipMaxThumbnailWidth = Program.MaxThumbnailWidthVIP ?? 0,
				VipMaxThumbnailSizeKB = Program.MaxThumbnailSizeKBVIP ?? 0
			});
		}

		/*Me response:
		  200 OK + MeResponse for a valid bearer token.
		  401 Unauthorized when the header is missing or the token is invalid/expired.
		  Lets a client refresh its VIP tier at startup without forcing a re-login. A caller must
		  treat any failure - including 401 - as "no news" and keep its last known value.
		*/
		[HttpGet("me")]
		public IActionResult Me() {
			// This server has no auth middleware; every protected action checks the header itself.
			var authHeader = Request.Headers["Authorization"].ToString();
			if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
				return Unauthorized("Missing or invalid Authorization header.");

			var token = authHeader.Substring("Bearer ".Length).Trim();
			var username = PhotoTag.Server.JwtHelper.ValidateAndGetUsername(token, JwtSecret);
			if (string.IsNullOrEmpty(username))
				return Unauthorized("Invalid or expired token, or username missing.");

			return Ok(new MeResponse {
				Username = username,
				IsVip = Program.IsVip(username)
			});
		}

		// Disabled: unlike every other endpoint here, this had no bearer-token check tying the
		// request to the account being changed, and the old-password check wasn't wired into the
		// /login lockout - so it let anyone brute-force any account's password with no rate limit
		// at all. Nothing calls it client-side. Re-enable only after adding both of those back.
		[HttpPost("changepassword")]
		public IActionResult ChangePassword([FromBody] ChangePasswordRequest req) {
			return StatusCode(403, "This endpoint is currently disabled.");
		}

	  /*Register responses:
		200 OK + LoginResponse = user created successfully.
		400 BadRequest:
			REG_NO_DATA
			REG_USERNAME_INVALID          username missing, shorter than MinUsernameLength, or has illegal characters
			REG_PASSWORD_REQUIRED
			REG_USERNAME_EXISTS
			REG_EMAIL_REQUIRED            only when MANDATORY_EMAIL=true
			REG_EMAIL_INVALID             only checked when an email was actually supplied
			REG_EMAIL_EXISTS
			REG_VERIFICATION_EMAIL_SEND_FAILED
			REG_CODE_SENT                 a new code was generated and emailed
			REG_CODE_REQUIRED<seconds>    a code was already sent earlier and is still valid; NO new mail was sent
			REG_CODE_INVALID_OR_EXPIRED
			REG_CODE_LEFT<n>
			REG_CODE_TIMEOUT<seconds>.
	  */
		[HttpPost("register")]
		public async Task<IActionResult> Register([FromBody] RegisterRequest req) {
			LogDebug($"[REGISTER] New registration request: Username={req?.Username}, Email={req?.Email}, HasVerificationCode={!string.IsNullOrWhiteSpace(req?.VerificationCode)}");
			
			if (req == null)
				return BadRequest("REG_NO_DATA");

			// Username must be validated before it is used anywhere else: the character check
			// below throws on a null username, and an all-whitespace name would otherwise pass
			// it (Enumerable.All is true for an empty sequence) and create an unnamed user
			// directory under the user-data root.
			if (string.IsNullOrWhiteSpace(req.Username)
				|| req.Username.Length < MinUsernameLength
				|| !req.Username.All(c => char.IsLetterOrDigit(c) || c == '_'))
				return BadRequest("REG_USERNAME_INVALID");

			if (string.IsNullOrWhiteSpace(req.Password))
				return BadRequest("REG_PASSWORD_REQUIRED");

			// An empty email is allowed unless the server demands one. Format and uniqueness are
			// therefore only meaningful when the user actually supplied an address - checking them
			// unconditionally is what made MANDATORY_EMAIL=false unreachable, and what made a blank
			// email collide with existing users that have no email stored.
			var hasEmail = !string.IsNullOrWhiteSpace(req.Email);
			if (Program.ServerDemandsEmail && !hasEmail)
				return BadRequest("REG_EMAIL_REQUIRED");
			if (hasEmail && !IsValidEmail(req.Email))
				return BadRequest("REG_EMAIL_INVALID");

			var users = LoadUsers();
			if (users.Any(u => u.Username == req.Username))
				return BadRequest("REG_USERNAME_EXISTS");
			if (hasEmail && users.Any(u => !string.IsNullOrWhiteSpace(u.Email) && u.Email == req.Email))
				return BadRequest("REG_EMAIL_EXISTS");

			var passwordSalt = GenerateSalt();
			var passwordHash = HashPasswordSalted(req.Password, passwordSalt);

			// Email verification flow
			if (Program.EmailVerificationWanted) {
				// If no verification code provided => start flow: generate code, send email, store pending and abort registration
				if (string.IsNullOrWhiteSpace(req.VerificationCode)) {
					LogDebug($"[REGISTER] Phase 1: Generating verification code for {req.Username}");
					string generatedCode = null;
					string fromAddress = null;
					string subject = null;
					string body = null;

					lock (_pendingLock) {
						// Drop expired pendings first. Without this an abandoned pending record keeps
						// matching the duplicate check below forever, so the user is told a code was
						// already sent while phase 2 rejects that code as expired - an unrecoverable
						// state for that username.
						_pendingRegistrations.RemoveAll(p => p.ExpiresAtUtc < DateTime.UtcNow);

						// A pending registration already exists: report how long the earlier code stays
						// valid and do NOT send a second mail.
						var existingPending = _pendingRegistrations.FirstOrDefault(p => p.Username.Equals(req.Username, StringComparison.OrdinalIgnoreCase) || (hasEmail && !string.IsNullOrWhiteSpace(p.Email) && p.Email.Equals(req.Email, StringComparison.OrdinalIgnoreCase)));
						if (existingPending != null) {
							var remaining = (int)Math.Max(0, (existingPending.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds);
							LogDebug($"[REGISTER] Duplicate pending registration for {req.Username}, {remaining}s of validity left");
							return BadRequest("REG_CODE_REQUIRED" + remaining);
						}
						// generate 6-digit numeric code
						generatedCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
						LogDebug($"[REGISTER] Generated code: {generatedCode} for {req.Username}");
						var pending = new PendingRegistration {
							Username = req.Username,
							Email = req.Email,
							Code = generatedCode,
							CreatedAtUtc = DateTime.UtcNow,
							ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
							Attempts = 0,
							ClientIp = GetClientIp()
						};
						_pendingRegistrations.Add(pending);

						// Build email (inside lock for thread safety)
						subject = "Your PhotoTag verification code";
						try {
							var templatePath = Path.Combine(AppContext.BaseDirectory, "templates", "emailcode.html");
							if (System.IO.File.Exists(templatePath)) {
							LogDebug($"[EMAIL] Loading template from {templatePath}");
							var template = System.IO.File.ReadAllText(templatePath);
							body = template.Replace("{{CODE}}", WebUtility.HtmlEncode(generatedCode)).Replace("{{USERNAME}}", WebUtility.HtmlEncode(req.Username ?? string.Empty));
						} else {
							LogDebug($"[EMAIL] Template not found at {templatePath}, using default body");
								body = $"Your verification code: <strong>{generatedCode}</strong>\n\nThis code expires in 15 minutes.";
							}
						} catch {
							body = $"Your verification code: <strong>{generatedCode}</strong>\n\nThis code expires in 15 minutes.";
						}
						fromAddress = string.IsNullOrWhiteSpace(Program.SmtpUsername) ? "phototag@tobikcze.eu" : Program.SmtpUsername;
						LogDebug($"[EMAIL] Using From address: {fromAddress}");
					}

					// Send email outside of lock (where we can await) using MailKit
					LogDebug($"[EMAIL] Sending verification email to {req.Email} for user {req.Username} via MailKit");
					try {
						var message = new MimeMessage();
						try {
							message.From.Add(new MailboxAddress("PhotoTag", fromAddress));
						} catch {
							message.From.Add(MailboxAddress.Parse(fromAddress));
						}
						message.To.Add(MailboxAddress.Parse(req.Email));
						message.ReplyTo.Add(new MailboxAddress("PhotoTag", "phototag@tobikcze.eu"));
						message.Subject = subject;
						message.Body = new TextPart("html") { Text = body };

						using (var client = new MailKit.Net.Smtp.SmtpClient()) {
							try {
								// Enable verbose logging for SMTP
								client.ServerCertificateValidationCallback = (s, c, ch, e) => true;
								
								var secure = Program.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
								LogDebug($"[EMAIL] Connecting to {Program.SmtpHost}:{Program.SmtpPort} with {secure}");
								await client.ConnectAsync(Program.SmtpHost, Program.SmtpPort, secure);
								LogDebug($"[EMAIL] Connected successfully");
								
								if (!string.IsNullOrWhiteSpace(Program.SmtpUsername)) {
									LogDebug($"[EMAIL] Authenticating as {Program.SmtpUsername}");
									await client.AuthenticateAsync(Program.SmtpUsername, Program.SmtpPassword);
									LogDebug($"[EMAIL] Authentication successful");
								}
								
								LogDebug($"[EMAIL] Sending message FROM {message.From} TO {message.To}");
								var response = await client.SendAsync(message);
								LogDebug($"[EMAIL] Server response: {response}");
								
								await client.DisconnectAsync(true);
								LogDebug($"[EMAIL] MailKit send completed for {req.Email}");
							} catch (Exception smtpEx) {
								LogDebug($"[EMAIL] MailKit send error: {smtpEx.GetType().Name}: {smtpEx.Message}");
								LogDebug($"[EMAIL] Stack trace: {smtpEx.StackTrace}");
								// remove pending on send failure
								lock (_pendingLock) {
									_pendingRegistrations.RemoveAll(p => p.Username.Equals(req.Username, StringComparison.OrdinalIgnoreCase));
								}
								return BadRequest("REG_VERIFICATION_EMAIL_SEND_FAILED");
							}
						}
					} catch (Exception ex) {
						LogDebug($"[EMAIL] ERROR building/sending email to {req.Email}: {ex}");
						lock (_pendingLock) {
							_pendingRegistrations.RemoveAll(p => p.Username.Equals(req.Username, StringComparison.OrdinalIgnoreCase));
						}
						return BadRequest("REG_VERIFICATION_EMAIL_SEND_FAILED");
					}

					return BadRequest("REG_CODE_SENT");
				}

				// If verification code was provided, validate it against pending
				LogDebug($"[REGISTER] Phase 2: Verifying code for {req.Username}");
				lock (_pendingLock) {
					var pending = _pendingRegistrations.FirstOrDefault(p => p.Username.Equals(req.Username, StringComparison.OrdinalIgnoreCase) || (hasEmail && !string.IsNullOrWhiteSpace(p.Email) && p.Email.Equals(req.Email, StringComparison.OrdinalIgnoreCase)));
					if (pending == null || pending.ExpiresAtUtc < DateTime.UtcNow) {
						LogDebug($"[REGISTER] No pending registration found or code expired for {req.Username}");
						return BadRequest("REG_CODE_INVALID_OR_EXPIRED");
					}

					// check code (plaintext compare)
					if (!string.Equals(pending.Code, req.VerificationCode, StringComparison.Ordinal)) {
						// If already blocked, return remaining timeout without increasing attempts
						if (pending.BlockedUntilUtc.HasValue && pending.BlockedUntilUtc.Value > DateTime.UtcNow) {
							var secs = (int)Math.Max(0, (pending.BlockedUntilUtc.Value - DateTime.UtcNow).TotalSeconds);
							LogDebug($"[REGISTER] Attempt while blocked for {req.Username}, {secs}s remaining, ip={GetClientIp()}");
							return BadRequest("REG_CODE_TIMEOUT" + secs);
						}
						pending.Attempts++;
						pending.ClientIp = GetClientIp();
						LogDebug($"[REGISTER] Invalid code attempt {pending.Attempts}/5 for {req.Username} from IP {pending.ClientIp}");
						if (pending.Attempts >= 5) {
							pending.BlockedUntilUtc = DateTime.UtcNow.AddMinutes(10);
							var secs = (int)Math.Max(0, (pending.BlockedUntilUtc.Value - DateTime.UtcNow).TotalSeconds);
							LogDebug($"[REGISTER] Max attempts reached, blocking until {pending.BlockedUntilUtc} for {req.Username}, ip={pending.ClientIp}");
							return BadRequest("REG_CODE_TIMEOUT" + secs);
						}
						return BadRequest("REG_CODE_LEFT" + (5 - pending.Attempts));
					}

					// matched: remove pending and continue to create user
					LogDebug($"[REGISTER] Code verified successfully for {req.Username}");
					_pendingRegistrations.Remove(pending);
				}
			}

			// create the user now (either verification not required or verification succeeded)
			var user = new User {
				Username = req.Username,
				PasswordHash = passwordHash,
				PasswordSalt = passwordSalt
			};
			if(!string.IsNullOrEmpty(req.Email))
				user.Email = req.Email;

			users.Add(user);
			SaveUsers(users);
			Directory.CreateDirectory(Path.Combine(Program.UserDataPath, req.Username));
			LogDebug($"[REGISTER] User created successfully: {req.Username} (Email: {req.Email})");

			var token = GenerateJwtToken(user.Username);
			LogDebug($"[REGISTER] JWT token generated for {req.Username}");
			return Ok(new LoginResponse {
				Token = token,
				// A brand new user may already be on the VIP roster.
				IsVip = Program.IsVip(user.Username)
			});
		}


	  /*Login responses:
		200 OK + LoginResponse = login successful.
		400 BadRequest: 
			LOG_NO_DATA
			LOG_INVALID_CREDENTIALS
			LOG_TIMEOUT<seconds>.
	  */
		[HttpPost("login")]
		public IActionResult Login([FromBody] LoginRequest req) {
			if (req == null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
				return BadRequest("LOG_NO_DATA");
			var users = LoadUsers();

			var user = users.FirstOrDefault(u => u.Username == req.Username);
			if (user == null && IsValidEmail(req.Username)) {
				user = users.FirstOrDefault(u => u.Email == req.Username);
			}

			if (user == null)
				return BadRequest("LOG_INVALID_CREDENTIALS");

			var clientIp = GetClientIp();
			lock (_pendingLoginLock) {
				var pendingLogin = _pendingLogins.FirstOrDefault(p => p.Username.Equals(user.Username, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrWhiteSpace(user.Email) && !string.IsNullOrWhiteSpace(p.Email) && p.Email.Equals(user.Email, StringComparison.OrdinalIgnoreCase)));
				if (pendingLogin != null && pendingLogin.ExpiresAtUtc < DateTime.UtcNow) {
					_pendingLogins.Remove(pendingLogin);
					pendingLogin = null;
				}

				if (pendingLogin != null && pendingLogin.BlockedUntilUtc.HasValue && pendingLogin.BlockedUntilUtc.Value > DateTime.UtcNow) {
					var secs = (int)Math.Max(0, (pendingLogin.BlockedUntilUtc.Value - DateTime.UtcNow).TotalSeconds);
					LogDebug($"[LOGIN] Attempt while blocked for {user.Username}, {secs}s remaining, ip={clientIp}");
					return BadRequest("LOG_TIMEOUT" + secs);
				}

				if (!VerifyAndMigratePassword(user, req.Password, users)) {
					if (pendingLogin == null) {
						pendingLogin = new PendingRegistration {
							Username = user.Username,
							Email = user.Email,
							CreatedAtUtc = DateTime.UtcNow,
							ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
							Attempts = 0,
							ClientIp = clientIp
						};
						_pendingLogins.Add(pendingLogin);
					}

					pendingLogin.Attempts++;
					pendingLogin.ClientIp = clientIp;
					pendingLogin.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30);
					LogDebug($"[LOGIN] Invalid password attempt {pendingLogin.Attempts}/5 for {user.Username} from IP {pendingLogin.ClientIp}");
					if (pendingLogin.Attempts >= 5) {
						pendingLogin.BlockedUntilUtc = DateTime.UtcNow.AddMinutes(5);
						var secs = (int)Math.Max(0, (pendingLogin.BlockedUntilUtc.Value - DateTime.UtcNow).TotalSeconds);
						LogDebug($"[LOGIN] Max attempts reached, blocking until {pendingLogin.BlockedUntilUtc} for {user.Username}, ip={pendingLogin.ClientIp}");
						return BadRequest("LOG_TIMEOUT" + secs);
					}

					return BadRequest("LOG_INVALID_CREDENTIALS");
				}

				if (pendingLogin != null)
					_pendingLogins.Remove(pendingLogin);
			}

			var token = GenerateJwtToken(user.Username);
			return Ok(new LoginResponse {
				Token = token,
				IsVip = Program.IsVip(user.Username)
			});
		}
		private void LogError(string message, Exception ex) {
			Console.Error.WriteLine(message, ex);
		}

		private string GetClientIp() {
			try {
				if (Request?.Headers != null) {
					if (Request.Headers.TryGetValue("CF-Connecting-IP", out var cf) && !string.IsNullOrWhiteSpace(cf))
						return cf.FirstOrDefault();
					if (Request.Headers.TryGetValue("X-Real-IP", out var xr) && !string.IsNullOrWhiteSpace(xr))
						return xr.FirstOrDefault();
					if (Request.Headers.TryGetValue("X-Forwarded-For", out var xff) && !string.IsNullOrWhiteSpace(xff)) {
						var first = xff.ToString().Split(',').Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s));
						if (!string.IsNullOrEmpty(first)) return first;
					}
					if (Request.Headers.TryGetValue("True-Client-IP", out var tc) && !string.IsNullOrWhiteSpace(tc))
						return tc.FirstOrDefault();
				}
			} catch { }
			try {
				var ip = HttpContext?.Connection?.RemoteIpAddress?.ToString();
				if (!string.IsNullOrEmpty(ip)) return ip;
			} catch { }
			return null;
		}

		// --- Helper methods ---
		private List<User> LoadUsers() {
			return _usersCache;
		}
		private void SaveUsers(List<User> users) {
			var userDataDir = Path.GetDirectoryName(UsersXmlPath);
			if (!Directory.Exists(userDataDir)) Directory.CreateDirectory(userDataDir);
			var serializer = new XmlSerializer(typeof(List<User>));
			using (var stream = System.IO.File.Create(UsersXmlPath))
				serializer.Serialize(stream, users);
		}
		// Original, unsalted scheme. Kept only to verify accounts that have not been migrated
		// yet (PasswordSalt is null) and as the inner step of the salted hash below - never used
		// on its own for a new or already-migrated account.
		private string HashPasswordLegacy(string password) {
			using (var sha = SHA256.Create()) {
				var bytes = Encoding.UTF8.GetBytes(password);
				var hash = sha.ComputeHash(bytes);
				return Convert.ToBase64String(hash);
			}
		}

		// SHA256(legacyHash + salt). Built on top of the legacy hash rather than the raw
		// password so that migrating an existing account costs nothing extra: its stored legacy
		// hash IS the first half of this computation, so a lucky rainbow-table hit against the
		// old unsalted value still doesn't recover the new salted one, and a fresh registration
		// goes through the exact same function as a migration - one code path, not two.
		private string HashPasswordSalted(string password, string salt) {
			var legacyHash = HashPasswordLegacy(password);
			using (var sha = SHA256.Create()) {
				var bytes = Encoding.UTF8.GetBytes(legacyHash + salt);
				var hash = sha.ComputeHash(bytes);
				return Convert.ToBase64String(hash);
			}
		}

		private string GenerateSalt() {
			return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
		}

		/// <summary>
		/// Verifies a login password against whichever scheme this account is currently stored
		/// under. A correct legacy password is exactly as trustworthy as a correct salted one, so
		/// a successful legacy verification also migrates the account in place - salting happens
		/// gradually as each user logs in, with no bulk migration and no forced password reset.
		/// </summary>
		private bool VerifyAndMigratePassword(User user, string password, List<User> users) {
			if (!string.IsNullOrEmpty(user.PasswordSalt)) {
				return HashPasswordSalted(password, user.PasswordSalt) == user.PasswordHash;
			}

			if (HashPasswordLegacy(password) != user.PasswordHash) {
				return false;
			}

			var salt = GenerateSalt();
			user.PasswordSalt = salt;
			user.PasswordHash = HashPasswordSalted(password, salt);
			SaveUsers(users);
			return true;
		}
		private string GenerateJwtToken(string username) {
			var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
			var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
			var claims = new[] {
				new Claim(ClaimTypes.Name, username),
				new Claim("sub", username),
				new Claim("unique_name", username)
			};
			var token = new JwtSecurityToken(
				issuer: "PhotoTag",
				audience: "PhotoTag",
				claims: claims,
				expires: DateTime.UtcNow.AddHours(24),
				signingCredentials: creds
			);
			return new JwtSecurityTokenHandler().WriteToken(token);
		}
	}
}
