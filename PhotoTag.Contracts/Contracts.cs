using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace PhotoTag.Contracts
{
    public enum ThumbnailSource
    {
        PhoneCamera,
        PhoneGallery,
        WebCamera,
        WebExternal
    }

    public enum GpsSource
    {
        PhoneApp,
        WebApp,
        WebManual,
        PhoneAppManual
    }

    public enum GpsAltType
    {
        ESM,
        WGS84
    }

    [XmlRoot("User")]
    public class User
    {
        public string Username { get; set; }
        public string Email {get; set;}
        public string PasswordHash { get; set; }
        // Null/empty means this account predates salting: PasswordHash is still the legacy
        // unsalted SHA256(password). It gets migrated to a salted hash in place on next
        // successful login - see UsersController.VerifyAndMigratePassword.
        public string PasswordSalt { get; set; }
        public string EncryptedMEK { get; set; }
        public string Salt { get; set; }
        public string IV { get; set; }
    }

    [XmlRoot("Manufacturer")]
    public class Manufacturer
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LogoFileName { get; set; }
        [XmlArray("FilmStocks")]
        [XmlArrayItem("FilmStock")]
        public List<FilmStock> FilmStocks { get; set; } = new List<FilmStock>();
        [XmlIgnore]
        public string LogoLocalPath { get; set; }
    }

    [XmlRoot("FilmStock")]
    public class FilmStock
    {
        public string Id { get; set; }
        public string NameShort { get; set; }
        public string NameFullUser { get; set; }
        public string IconFileName { get; set; }
        public int ISO { get; set; }
        [XmlIgnore]
        public string IconLocalPath { get; set; }
    }

    [XmlRoot("Roll")]
    public class Roll
    {
        public bool CreatedOnServer { get; set; }
        public int RollId { get; set; }
        public string DateCreatedUtc { get; set; } // "YYYY-MM-DD_HH_MM_SS"
        public int TimeZoneMinutes { get; set; }
        public FilmStock Stock { get; set; }
        public string NumberOfExposures { get; set; }
        public string CameraName { get; set; }
        public string Description { get; set; }
        public string PullPushInfo { get; set; }
        [XmlArray("SavedTimes")]
        [XmlArrayItem("SavedTime")]
        public List<SavedTime> SavedTimes { get; set; } = new List<SavedTime>();
    }

    [XmlRoot("Lens")]
    public class Lens {
        public string Id { get; set; }
        public string Name { get; set; }
        public string focalLength { get; set; } // e.g. "40mm", "35-80mm"
    }

    [XmlRoot("SavedTime")]
    public class SavedTime {
        public bool SyncedToServer { get; set; }
        public int ExposureNumber { get; set; }
        public string Description { get; set; }
        public Lens Lens { get; set; }
        public string TimeUtc { get; set; } // "YYYY-MM-DD_HH_MM_SS"
        public int TimeZoneMinutes { get; set; }
        public ThumbnailSource ThumbnailSource { get; set; }
        public GpsSource GpsSource { get; set; }
        public string DeviceName { get; set; }
        public float GpsLat { get; set; }
        public float GpsLon { get; set; }
        public string GpsLatEnc { get; set; }
        public string GpsLonEnc { get; set; }
        [XmlElement(IsNullable = true)]
        public float? GpsAlt { get; set; }
        public string GpsAltEnc { get; set; }
        [XmlElement(IsNullable = true)]
        public GpsAltType? GpsAltType { get; set; }
	[XmlElement(IsNullable = true)]
	public float? GpsImgDirection { get; set; }
        public string ThumbnailName { get; set; }
        public bool ThumbnailUploaded { get; set; }


        [XmlElement(IsNullable = true)]
        public float? ShutterSpeed { get; set; }

        [XmlElement(IsNullable = true)]
        public float? Aperture { get; set; }

        // Change this to nullable so the serializer doesn't complain if it's missing
        [XmlElement(IsNullable = true)]
        public float? GpsAcc { get; set; }
    }


    // --- DTOs ---
	public class ChangePasswordRequest {
		public string Username { get; set; }
		public string OldPassword { get; set; }
		public string NewPassword { get; set; }
		public string NewEncryptedMEK { get; set; }
		public string NewSalt { get; set; }
		public string NewIV { get; set; }
	}
	public class RegisterRequest {
		public string Username { get; set; }
        public string Email { get; set; }
		public string Password { get; set; }
        public string VerificationCode { get; set; }
	}
	public class LoginRequest {
		public string Username { get; set; }
		public string Password { get; set; }
	}
	public class LoginResponse {
		public string Token { get; set; }
		public string EncryptedMEK { get; set; }
		public string Salt { get; set; }
		public string IV { get; set; }
		public bool IsVip { get; set; }
	}
	// Answer to "who am I", so a client can re-check its tier without forcing a re-login.
	public class MeResponse {
		public string Username { get; set; }
		public bool IsVip { get; set; }
	}
	// Anonymous capability probe. Product/ApiVersion let a client tell a real PhotoTag server
	// apart from any other host that happens to answer 200 on the entered URL.
	public class ServerInfoResponse {
		public string Product { get; set; }
		public int ApiVersion { get; set; }
		public bool MandatoryEmail { get; set; }
		public bool EmailVerification { get; set; }
		public int MinUsernameLength { get; set; }
		// Thumbnail allowances. Width is advice for the client so it can pick a resolution that
		// lands under the byte cap; only the byte cap is enforced. The VIP pair is 0 when no VIP
		// tier is configured.
		public int MaxThumbnailWidth { get; set; }
		public int MaxThumbnailSizeKB { get; set; }
		public int VipMaxThumbnailWidth { get; set; }
		public int VipMaxThumbnailSizeKB { get; set; }
	}
}