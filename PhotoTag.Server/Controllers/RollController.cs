using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PhotoTag.Contracts;
using System.IO;
using System.Xml.Serialization;
using System.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace PhotoTag.Server.Controllers {
	[ApiController]
	[Route("api/[controller]")]
	public class RollController : ControllerBase {

		private static string UserDataRoot => Program.UserDataPath;
		private static string JwtSecret => Program.JwtSecret;

		private readonly ILogger<RollController> _logger;

		public RollController(ILogger<RollController> logger) {
			_logger = logger;
		}
        // CRC32 helper
        private static uint Crc32(string input) {
            uint crc = 0xFFFFFFFF;
            foreach (var b in Encoding.UTF8.GetBytes(input)) {
                crc ^= b;
                for (int i = 0; i < 8; i++) {
                    if ((crc & 1) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320;
                    else
                        crc >>= 1;
                }
            }
            return ~crc;
        }
		// POST api/roll/create
		[HttpPost("create")]
		public IActionResult CreateRoll() {
			_logger.LogInformation("[Roll] POST create called from {IP}", HttpContext.Connection.RemoteIpAddress);
			// Require Authorization header with Bearer token
			var authHeader = Request.Headers["Authorization"].ToString();
			if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ")) {
				_logger.LogWarning("[Roll] create: missing/invalid Authorization header");
				return Unauthorized("Missing or invalid Authorization header.");
			}

			var token = authHeader.Substring("Bearer ".Length).Trim();
			var username = PhotoTag.Server.JwtHelper.ValidateAndGetUsername(token, JwtSecret);
			if (string.IsNullOrEmpty(username)) {
				_logger.LogWarning("[Roll] create: invalid or expired token");
				return Unauthorized("Invalid or expired token, or username missing.");
			}
			_logger.LogInformation("[Roll] create: authenticated as '{Username}'", username);

			string userDir = Path.Combine(UserDataRoot, username);
			Directory.CreateDirectory(userDir);

			// Accept XML or JSON
			Roll roll = null;
			if (Request.ContentType != null && Request.ContentType.Contains("xml")) {
				// XML body
				try {
					var serializer = new XmlSerializer(typeof(Roll));
					roll = (Roll)serializer.Deserialize(Request.Body);
				} catch (Exception ex) {
					_logger.LogError(ex, "[Roll] create: XML parse error");
					return BadRequest("Invalid XML: " + ex.Message);
				}
			} else {
				// JSON body
				using (var reader = new StreamReader(Request.Body, Encoding.UTF8)) {
					var json = reader.ReadToEnd();
					_logger.LogDebug("[Roll] create JSON body: {Json}", json);
					try {
						roll = System.Text.Json.JsonSerializer.Deserialize<Roll>(json);
					} catch (Exception ex) {
						_logger.LogError(ex, "[Roll] create: JSON parse error");
						return BadRequest("Invalid JSON: " + ex.Message);
					}
				}
			}
			if (roll == null) {
				_logger.LogWarning("[Roll] create: deserialized roll is null");
				return BadRequest("No roll data.");
			}

			// Assign RollId if not set
			if (roll.RollId == 0) {
				var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				roll.RollId = (int)Crc32(now.ToString());
				_logger.LogInformation("[Roll] create: assigned new RollId={RollId}", roll.RollId);
			} else {
				_logger.LogInformation("[Roll] create: using provided RollId={RollId}", roll.RollId);
			}
            
			roll.CreatedOnServer = true;

			// Save as XML
			string xmlPath = Path.Combine(userDir, $"roll_{roll.RollId}.xml");
			var serializer2 = new XmlSerializer(typeof(Roll));
            using (var stream = new FileStream(xmlPath, FileMode.Create, FileAccess.Write, FileShare.None))
				serializer2.Serialize(stream, roll);

			_logger.LogInformation("[Roll] create: saved to '{Path}', SavedTimes={Count}", xmlPath, roll.SavedTimes?.Count ?? 0);
			return Ok(new {
				Success = true,
				RollId = roll.RollId,
				Path = xmlPath,
				SavedTimes = roll.SavedTimes?.Count ?? 0
			});
		}
        // POST api/roll/addphoto
        [HttpPost("addphoto")]
        public IActionResult AddPhoto([FromQuery] int rollId) {
            _logger.LogInformation("[Roll] POST addphoto called, rollId={RollId}, from {IP}", rollId, HttpContext.Connection.RemoteIpAddress);
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ")) {
                _logger.LogWarning("[Roll] addphoto: missing/invalid Authorization header");
                return Unauthorized("Missing or invalid Authorization header.");
            }
            var token = authHeader.Substring("Bearer ".Length).Trim();
            var username = PhotoTag.Server.JwtHelper.ValidateAndGetUsername(token, JwtSecret);
            if (string.IsNullOrEmpty(username)) {
                _logger.LogWarning("[Roll] addphoto: invalid or expired token");
                return Unauthorized("Invalid or expired token, or username missing.");
            }
            _logger.LogInformation("[Roll] addphoto: authenticated as '{Username}'", username);

            if (rollId <= 0) {
                _logger.LogWarning("[Roll] addphoto: missing or invalid rollId");
                return BadRequest("Missing or invalid rollId.");
            }

            string userDir = Path.Combine(UserDataRoot, username);
            Directory.CreateDirectory(userDir);
            string rollPath = Path.Combine(userDir, $"roll_{rollId}.xml");
            if (!System.IO.File.Exists(rollPath)) {
                _logger.LogWarning("[Roll] addphoto: roll file not found at '{RollPath}'", rollPath);
                return NotFound("Roll not found.");
            }

            SavedTime savedTime = null;
            if (Request.ContentType != null && Request.ContentType.Contains("xml")) {
                try {
                    var serializer = new XmlSerializer(typeof(SavedTime));
                    savedTime = (SavedTime)serializer.Deserialize(Request.Body);
                } catch (Exception ex) {
                    _logger.LogError(ex, "[Roll] addphoto: XML parse error");
                    return BadRequest("Invalid XML: " + ex.Message);
                }
            } else {
                using (var reader = new StreamReader(Request.Body, Encoding.UTF8)) {
                    var json = reader.ReadToEnd();
                    _logger.LogDebug("[Roll] addphoto JSON body: {Json}", json);
                    try {
                        savedTime = System.Text.Json.JsonSerializer.Deserialize<SavedTime>(json);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "[Roll] addphoto: JSON parse error");
                        return BadRequest("Invalid JSON: " + ex.Message);
                    }
                }
            }
            if (savedTime == null) {
                _logger.LogWarning("[Roll] addphoto: deserialized savedTime is null");
                return BadRequest("No photo data.");
            }
            _logger.LogInformation("[Roll] addphoto: received SavedTime exposure={Exposure}, time={Time}", savedTime.ExposureNumber, savedTime.TimeUtc);

            // Append (or update existing) saved time directly in the roll XML
            Roll roll;
            var rollSerializer = new XmlSerializer(typeof(Roll));
            using (var stream = new FileStream(rollPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                roll = (Roll)rollSerializer.Deserialize(stream);
            }

            if (roll == null) {
                _logger.LogError("[Roll] addphoto: roll XML at '{RollPath}' deserialized to null", rollPath);
                return BadRequest("Corrupted roll XML.");
            }

            if (roll.SavedTimes == null) {
                roll.SavedTimes = new List<SavedTime>();
            }

            var existing = roll.SavedTimes.FirstOrDefault(st =>
                st.ExposureNumber == savedTime.ExposureNumber);

            if (existing == null) {
                roll.SavedTimes.Add(savedTime);
                _logger.LogInformation("[Roll] addphoto: added new SavedTime, total={Count}", roll.SavedTimes.Count);
            } else {
                existing.SyncedToServer = savedTime.SyncedToServer;
                existing.TimeZoneMinutes = savedTime.TimeZoneMinutes;
                existing.GpsLat = savedTime.GpsLat;
                existing.GpsLon = savedTime.GpsLon;
                existing.GpsLatEnc = savedTime.GpsLatEnc;
                existing.GpsLonEnc = savedTime.GpsLonEnc;
                existing.ThumbnailName = savedTime.ThumbnailName;
                existing.ThumbnailUploaded = savedTime.ThumbnailUploaded;
                existing.Description = savedTime.Description;
                _logger.LogInformation("[Roll] addphoto: updated existing SavedTime exposure={Exposure}", existing.ExposureNumber);
            }

            using (var stream = new FileStream(rollPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                rollSerializer.Serialize(stream, roll);
            }

            _logger.LogInformation("[Roll] addphoto: saved roll, SavedTimes={Count}", roll.SavedTimes.Count);
            return Ok(new { Success = true, RollId = rollId, SavedTimes = roll.SavedTimes.Count });
        }

        // POST api/roll/uploadthumbnail
        [HttpPost("uploadthumbnail")]
        public IActionResult UploadThumbnail() {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized("Missing or invalid Authorization header.");
            var token = authHeader.Substring("Bearer ".Length).Trim();
            var username = PhotoTag.Server.JwtHelper.ValidateAndGetUsername(token, JwtSecret);
            if (string.IsNullOrEmpty(username)) return Unauthorized("Invalid or expired token, or username missing.");

            string userDir = Path.Combine(UserDataRoot, username);
            Directory.CreateDirectory(userDir);

            var file = Request.Form.Files["file"];
            if (file == null) return BadRequest("No file uploaded.");

            // Size is the only thing enforced. The configured max width is advice the client
            // uses to pick a resolution that lands under this cap - deliberately do NOT decode
            // the upload or check its pixel dimensions. A huge image that compresses under the
            // byte cap is perfectly acceptable; the server just stores an opaque blob.
            int maxThumbWidth, maxThumbSizeKB;
            Program.GetThumbnailLimits(username, out maxThumbWidth, out maxThumbSizeKB);
            var maxThumbBytes = maxThumbSizeKB * 1024;
            if (file.Length > maxThumbBytes) return StatusCode(413, "THUMB_TOO_LARGE" + maxThumbBytes);

            // Strict allow-list for the whole filename, not a blacklist of "bad" characters:
            // anything containing a path separator, "..", a drive letter, or any other shape
            // simply fails to match and is rejected before it ever reaches Path.Combine. This
            // used to accept the client-supplied filename as-is, which let it write outside
            // userDir entirely (e.g. into the statically-served ClientHTML folder).
            var fileNameMatch = System.Text.RegularExpressions.Regex.Match(
                file.FileName ?? string.Empty,
                @"^roll_(\d+)_(\d+)\.(jpg|enc)$");
            if (!fileNameMatch.Success) return BadRequest("Invalid file name.");

            var rollId = fileNameMatch.Groups[1].Value;
            var exposureNumber = int.Parse(fileNameMatch.Groups[2].Value);

            string savePath = Path.Combine(userDir, file.FileName);
            using (var stream = new FileStream(savePath, FileMode.Create))
                file.CopyTo(stream);

            //load roll from xml
            string rollPath = Path.Combine(userDir, $"roll_{rollId}.xml");
            if (!System.IO.File.Exists(rollPath)) {
                return NotFound("Roll not found for rollId: " + rollId);
            }
            Roll roll;
            var rollSerializer = new XmlSerializer(typeof(Roll));
            using (var stream = new FileStream(rollPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                roll = (Roll)rollSerializer.Deserialize(stream);
            }
            //set thumbnail uploaded flag to true
            var savedTime = roll.SavedTimes.FirstOrDefault(st => st.ExposureNumber == exposureNumber);
            if (savedTime != null) {
                savedTime.ThumbnailUploaded = true;
                using (var stream = new FileStream(rollPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    rollSerializer.Serialize(stream, roll);
                }
            }

            return Ok(new { Success = true, Path = savePath });
        }

        // DELETE api/roll/deletephoto?rollId=X&exposureNumber=N
        [HttpDelete("deletephoto")]
        public IActionResult DeletePhoto([FromQuery] int rollId, [FromQuery] int exposureNumber) {
            _logger.LogInformation("[Roll] DELETE deletephoto called, rollId={RollId}, exposureNumber={ExposureNumber}, from {IP}", rollId, exposureNumber, HttpContext.Connection.RemoteIpAddress);

            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ")) {
                return Unauthorized("Missing or invalid Authorization header.");
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var username = PhotoTag.Server.JwtHelper.ValidateAndGetUsername(token, JwtSecret);
            if (string.IsNullOrEmpty(username)) {
                return Unauthorized("Invalid or expired token, or username missing.");
            }

            if (rollId <= 0 || exposureNumber <= 0) {
                return BadRequest("Missing or invalid rollId or exposureNumber.");
            }

            string userDir = Path.Combine(UserDataRoot, username);
            Directory.CreateDirectory(userDir);
            string rollPath = Path.Combine(userDir, $"roll_{rollId}.xml");
            if (!System.IO.File.Exists(rollPath)) {
                return NotFound("Roll not found.");
            }

            Roll roll;
            var rollSerializer = new XmlSerializer(typeof(Roll));
            using (var stream = new FileStream(rollPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
                roll = (Roll)rollSerializer.Deserialize(stream);
            }

            if (roll == null || roll.SavedTimes == null) {
                return BadRequest("Corrupted roll XML.");
            }

            var target = roll.SavedTimes.FirstOrDefault(st => st.ExposureNumber == exposureNumber);
            if (target == null) {
                return NotFound("Saved time not found.");
            }

            DeleteThumbnailFileIfPresent(userDir, rollId, target.ExposureNumber);
            roll.SavedTimes.Remove(target);

            var laterSavedTimes = roll.SavedTimes
                .Where(st => st.ExposureNumber > exposureNumber)
                .OrderBy(st => st.ExposureNumber)
                .ToList();

            foreach (var savedTime in laterSavedTimes) {
                var oldExposureNumber = savedTime.ExposureNumber;
                var newExposureNumber = oldExposureNumber - 1;
                RenameThumbnailFileIfPresent(userDir, rollId, oldExposureNumber, newExposureNumber);
                savedTime.ExposureNumber = newExposureNumber;
                if (savedTime.ThumbnailUploaded) {
                    savedTime.ThumbnailName = newExposureNumber + ".jpg";
                }
            }

            roll.SavedTimes = roll.SavedTimes.OrderBy(st => st.ExposureNumber).ToList();

            using (var stream = new FileStream(rollPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
                rollSerializer.Serialize(stream, roll);
            }

            _logger.LogInformation("[Roll] deletephoto: deleted exposure={ExposureNumber}, remaining={Count}", exposureNumber, roll.SavedTimes.Count);
            return Ok(new { Success = true, RollId = rollId, Remaining = roll.SavedTimes.Count });
        }

        private static void DeleteThumbnailFileIfPresent(string userDir, int rollId, int exposureNumber) {
            var filePath = Path.Combine(userDir, $"roll_{rollId}_{exposureNumber}.jpg");
            if (System.IO.File.Exists(filePath)) {
                System.IO.File.Delete(filePath);
            }
        }

        private static void RenameThumbnailFileIfPresent(string userDir, int rollId, int oldExposureNumber, int newExposureNumber) {
            var oldPath = Path.Combine(userDir, $"roll_{rollId}_{oldExposureNumber}.jpg");
            if (!System.IO.File.Exists(oldPath)) {
                return;
            }

            var newPath = Path.Combine(userDir, $"roll_{rollId}_{newExposureNumber}.jpg");
            if (System.IO.File.Exists(newPath)) {
                System.IO.File.Delete(newPath);
            }

            System.IO.File.Move(oldPath, newPath);
        }

        // GET api/roll/getrolls
        [HttpGet("getrolls")]
        public IActionResult GetRolls() {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized("Missing or invalid Authorization header.");

            var token = authHeader.Substring("Bearer ".Length).Trim();
            var username = PhotoTag.Server.JwtHelper.ValidateAndGetUsername(token, JwtSecret);
            if (string.IsNullOrEmpty(username))
                return Unauthorized("Invalid or expired token, or username missing.");

            string userDir = Path.Combine(UserDataRoot, username);
            Directory.CreateDirectory(userDir);

            var rollIds = Directory.GetFiles(userDir, "roll_*.xml")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name) && name.StartsWith("roll_", StringComparison.OrdinalIgnoreCase))
                .Select(name => name.Substring("roll_".Length))
                .ToList();

            return Ok(new {
                Success = true,
                RollIds = rollIds
            });
        }

        // GET api/roll/getroll?rollId=12345
        [HttpGet("getroll")]
        public IActionResult GetRoll([FromQuery] int rollId) {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized("Missing or invalid Authorization header.");
            var token = authHeader.Substring("Bearer ".Length).Trim();
            var username = PhotoTag.Server.JwtHelper.ValidateAndGetUsername(token, JwtSecret);
            if (string.IsNullOrEmpty(username)) return Unauthorized("Invalid or expired token, or username missing.");

            string userDir = Path.Combine(UserDataRoot, username);
            string xmlPath = Path.Combine(userDir, $"roll_{rollId}.xml");
            if (!System.IO.File.Exists(xmlPath)) return NotFound("Roll not found.");
            var xml = System.IO.File.ReadAllText(xmlPath, Encoding.UTF8);
            return Content(xml, "application/xml");
        }

        // GET api/roll/getthumbnail?rollId=X&exposureNumber=N
        [HttpGet("getthumbnail")]
        public IActionResult GetThumbnail([FromQuery] int rollId, [FromQuery] int exposureNumber) {
            var authHeader = Request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized("Missing or invalid Authorization header.");
            var token = authHeader.Substring("Bearer ".Length).Trim();
            var username = PhotoTag.Server.JwtHelper.ValidateAndGetUsername(token, JwtSecret);
            if (string.IsNullOrEmpty(username)) return Unauthorized("Invalid or expired token, or username missing.");

            if (rollId <= 0 || exposureNumber <= 0)
                return BadRequest("Missing or invalid rollId or exposureNumber.");

            string userDir = Path.Combine(UserDataRoot, username);
            string fileName = $"roll_{rollId}_{exposureNumber}.jpg";
            string filePath = Path.Combine(userDir, fileName);
            if (!System.IO.File.Exists(filePath)) return NotFound("Thumbnail not found.");
            return PhysicalFile(filePath, "image/jpeg");
        }
	}
}
