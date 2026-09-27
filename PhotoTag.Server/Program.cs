using System;
using System.Diagnostics;
using System.IO;
using FluentEmail.MailKitSmtp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PhotoTag.Server {
    public class Program {
        public static string UserDataPath { get; private set; }
        public static string JwtSecret { get; private set; }


        public static int MaxThumbnailWidth;
        public static int MaxThumbnailSizeKB;
        public static int? MaxThumbnailWidthVIP = null;
        public static int? MaxThumbnailSizeKBVIP = null;

        // Usernames listed in VIP_USERS. Membership is config, not user data, so changing it
        // needs a server restart - same as every other setting in config.txt.
        public static HashSet<string> VipUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool IsVip(string username) {
            return !string.IsNullOrWhiteSpace(username) && VipUsers.Contains(username.Trim());
        }

        /// <summary>
        /// The thumbnail allowance for one user. Single source of truth: both the upload
        /// validator and the serverinfo endpoint go through here so they cannot disagree.
        /// MaxWidth is advice for the client only - the server never inspects image dimensions.
        /// </summary>
        public static void GetThumbnailLimits(string username, out int maxWidth, out int maxSizeKB) {
            if (IsVip(username) && MaxThumbnailWidthVIP.HasValue && MaxThumbnailSizeKBVIP.HasValue) {
                maxWidth = MaxThumbnailWidthVIP.Value;
                maxSizeKB = MaxThumbnailSizeKBVIP.Value;
                return;
            }

            maxWidth = MaxThumbnailWidth;
            maxSizeKB = MaxThumbnailSizeKB;
        }
        public static bool ServerDemandsEmail;

        public static bool EmailVerificationWanted;
        public static string SmtpHost;
        public static int SmtpPort;
        public static string SmtpUsername;
        public static string SmtpPassword;

        public static void Main(string[] args) {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            generatexml();
            UserDataPath = GetEnvValue(Path.Combine(exeDir, "envsecrets.txt"), "USERDATA_PATH");
            JwtSecret = LoadSecret(Path.Combine(exeDir, "envsecrets.txt"));

            string clientHtmlPath = Path.Combine(exeDir, "ClientHTML");


            string port = "8069";
            string ip = "0.0.0.0";
            //czech config.txt file for port to overwrite default port
            string configPath = Path.Combine(exeDir, "config.txt");
            string portMaybe = GetEnvValue(configPath, "PORT");
            if (!string.IsNullOrEmpty(portMaybe)) {
                port = portMaybe;
            }
            string ipMaybe = GetEnvValue(configPath, "IP");

            ServerDemandsEmail = GetEnvValue(configPath, "MANDATORY_EMAIL") == "true";
            EmailVerificationWanted = GetEnvValue(configPath, "EMAIL_VERIFICATION") == "true";
            SmtpHost = GetEnvValue(configPath, "SMTP_HOST");
            SmtpPort = int.TryParse(GetEnvValue(configPath, "SMTP_PORT"), out int smtpPort) ? smtpPort : 587;
            SmtpUsername = GetEnvValue(configPath, "SMTP_USERNAME");
            SmtpPassword = GetEnvValue(configPath, "SMTP_PASSWORD");

            string maxThumbWidthMaybe = GetEnvValue(configPath, "MAX_THUMB_WIDTH");
            if (!string.IsNullOrEmpty(maxThumbWidthMaybe) && int.TryParse(maxThumbWidthMaybe, out int maxWidth)) {
                MaxThumbnailWidth = maxWidth;
            } else {
                MaxThumbnailWidth = 400;
            }

            string maxThumbSizeMaybe = GetEnvValue(configPath, "MAX_THUMB_SIZE_KB");
            if (!string.IsNullOrEmpty(maxThumbSizeMaybe) && int.TryParse(maxThumbSizeMaybe, out int maxSizeKB)) {
                MaxThumbnailSizeKB = maxSizeKB;
            } else {
                MaxThumbnailSizeKB = 15;
            }

            string maxThumbWidthVIPMaybe = GetEnvValue(configPath, "MAX_THUMB_WIDTH_VIP");
            if (!string.IsNullOrEmpty(maxThumbWidthVIPMaybe) && int.TryParse(maxThumbWidthVIPMaybe, out int maxWidthVIP)) {
                MaxThumbnailWidthVIP = maxWidthVIP;
            }
            string maxThumbSizeVIPMaybe = GetEnvValue(configPath, "MAX_THUMB_SIZE_KB_VIP");
            if (!string.IsNullOrEmpty(maxThumbSizeVIPMaybe) && int.TryParse(maxThumbSizeVIPMaybe, out int maxSizeKBVIP)) {
                MaxThumbnailSizeKBVIP = maxSizeKBVIP;
            }
            if ((MaxThumbnailWidthVIP.HasValue && !MaxThumbnailSizeKBVIP.HasValue) || (!MaxThumbnailWidthVIP.HasValue && MaxThumbnailSizeKBVIP.HasValue)) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("WARN: Only one of the VIP thumbnail settings is set. Please set both or none.");
                MaxThumbnailSizeKBVIP = null;
                MaxThumbnailWidthVIP = null;
                Console.ResetColor();
            }

            string vipUsersMaybe = GetEnvValue(configPath, "VIP_USERS");
            VipUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(vipUsersMaybe)) {
                foreach (var name in vipUsersMaybe.Split(',')) {
                    var trimmed = name.Trim();
                    if (trimmed.Length > 0) {
                        VipUsers.Add(trimmed);
                    }
                }
            }

            var vipLimitsConfigured = MaxThumbnailWidthVIP.HasValue && MaxThumbnailSizeKBVIP.HasValue;
            if (VipUsers.Count > 0 && !vipLimitsConfigured) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("WARN: VIP_USERS is set but no VIP thumbnail limits are configured. Those users will get the normal limits.");
                Console.ResetColor();
            }
            if (VipUsers.Count == 0 && vipLimitsConfigured) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("WARN: VIP thumbnail limits are configured but VIP_USERS is empty, so nobody is eligible.");
                Console.ResetColor();
            }

            //if Email verification is wanted, but SMTP settings are missing, then disable email verification and print warning
            if (EmailVerificationWanted && (string.IsNullOrEmpty(SmtpHost) || string.IsNullOrEmpty(SmtpUsername) || string.IsNullOrEmpty(SmtpPassword))) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("WARN: Email verification is enabled, but SMTP settings are missing. Disabling email verification.");
                EmailVerificationWanted = false;
                Console.ResetColor();
            }

            // Email verification needs an address to send the code to, so it implies a mandatory
            // email. Resolving this per-request instead would let any client skip verification
            // simply by omitting the field.
            if (EmailVerificationWanted && !ServerDemandsEmail) {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("WARN: Email verification is enabled, so an email address is required. Enabling mandatory email.");
                ServerDemandsEmail = true;
                Console.ResetColor();
            }


            string usedUrl = $"http://{ip}:{port}";

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("===================================================================================");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(" -- PhotoTag Deluxe Server --");
            Console.WriteLine($"Server is running on {usedUrl}");
            Console.WriteLine($"IP and port may be changed in config.txt");
            Console.WriteLine($"Please enter this URL into the Phototag Deluxe application for local network use,\nor port forward it in your router/tunnel");
            if(ip == "0.0.0.0") {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Warning: Server is listening on all interfaces (0.0.0.0).");
                Console.WriteLine("Please check ipconfig for your specific local IP and use that");
                Console.ForegroundColor = ConsoleColor.Cyan;
            }
            Console.WriteLine();
            Console.WriteLine($"Max thumbnail width: {MaxThumbnailWidth}px");
            Console.WriteLine($"Max thumbnail size: {MaxThumbnailSizeKB}KB");
            if (MaxThumbnailWidthVIP.HasValue && MaxThumbnailSizeKBVIP.HasValue) {
                Console.WriteLine("VIP user thumbnail configured:");
                Console.WriteLine($"VIP Max thumbnail width: {MaxThumbnailWidthVIP.Value}px");
                Console.WriteLine($"VIP Max thumbnail size: {MaxThumbnailSizeKBVIP.Value}KB");
                Console.WriteLine($"VIP users: {VipUsers.Count}");
            } else {
                Console.WriteLine("VIP mode off");
            }

            Console.WriteLine($"Mandatory email for registration: {ServerDemandsEmail}");
            Console.WriteLine($"Serving static files from: {clientHtmlPath}");
            Console.WriteLine($"User data is stored at: {UserDataPath}");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"JWT secret is: {(JwtSecret.Length > 10 ? JwtSecret.Substring(0, 10) + "..." : JwtSecret)}");
            Console.WriteLine($"Please dont forget to change the JWT secret in envsecrets.txt!");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"(If JWT secret is later changed after initial setup, all users will need to relog)");
            Console.ForegroundColor= ConsoleColor.Cyan;
            Console.WriteLine("===================================================================================");
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Example config.txt content:");
            Console.WriteLine("PORT=8069");
            Console.WriteLine("IP=192.168.68.75");
            Console.WriteLine("MANDATORY_EMAIL=true");
            Console.WriteLine("EMAIL_VERIFICATION=true");
            Console.WriteLine("SMTP_HOST=smtp.example.com");
            Console.WriteLine("SMTP_PORT=587");
            Console.WriteLine("SMTP_USERNAME=your_email_address");
            Console.WriteLine("SMTP_PASSWORD=your_password_password");
            Console.WriteLine("MAX_THUMB_WIDTH=400 //NOTE: Maximum value is 1920px!");
            Console.WriteLine("MAX_THUMB_SIZE_KB=15");
            Console.WriteLine("MAX_THUMB_WIDTH_VIP=1920 //User can be marked VIP for better thumb settings");
            Console.WriteLine("MAX_THUMB_SIZE_KB_VIP=100");
            Console.WriteLine("VIP_USERS=alice,bob //Comma-separated usernames that get the VIP limits");
            Console.WriteLine();
            Console.ResetColor();

            Stopwatch sw = new Stopwatch();
            sw.Start();
            Console.WriteLine("Starting server...");


            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(usedUrl);

            Console.WriteLine("Configuring Kestrel server...");
            builder.WebHost.ConfigureKestrel(options => {
                options.AllowSynchronousIO = true;
            });
            builder.Services.AddControllers().AddXmlSerializerFormatters();
            // Every action in this server validates its own bound input and returns a
            // machine-readable code (REG_*/LOG_*). The automatic [ApiController] model-state
            // filter would short-circuit before the action body and answer with a ProblemDetails
            // envelope instead, which made REG_NO_DATA/LOG_NO_DATA unreachable.
            builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options => {
                options.SuppressModelStateInvalidFilter = true;
            });
            builder.Services.AddCors(options => {
                options.AddPolicy("AllowAll", policy => policy
                    .AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader());
            });
            if(EmailVerificationWanted) {
                Console.WriteLine("Email verification enabled; using MailKit directly (no FluentEmail registration).");
            }
            var app = builder.Build();
            app.UseCors("AllowAll");

            Console.WriteLine("Setting up static file serving...");
            if (Directory.Exists(clientHtmlPath)) {
                app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions {
                    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(clientHtmlPath),
                    RequestPath = ""
                });
            }

            Console.WriteLine("Setting up routes...");
            app.MapGet("/", context => {
                context.Response.Redirect("/index.html");
                return System.Threading.Tasks.Task.CompletedTask;
            });
            app.MapControllers();

            Console.WriteLine("Initializing user data...");
            PhotoTag.Server.Controllers.UsersController.InitializeUsers();

            Console.WriteLine("SERVER READY! :-)");
            sw.Stop();
            Console.WriteLine($"Startup completed in {sw.ElapsedMilliseconds}ms");

            app.Run();
        }

        private static string LoadSecret(string path) {
            if (!File.Exists(path)) return "default-secret-change-me";
            var lines = File.ReadAllLines(path);
            foreach (var line in lines) {
                if (line.StartsWith("JWT_SECRET="))
                    return line.Substring("JWT_SECRET=".Length).Trim();
            }
            return "default-secret-change-me";
        }

        private static string GetEnvValue(string path, string key) {
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            foreach (var line in lines) {
                if (line.StartsWith(key + "="))
                    return line.Substring((key + "=").Length).Trim();
            }
            return "";
        }

        private static string generatexml() {
            return "";
        }
    }
}
