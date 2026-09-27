using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Xml.Serialization;
using System.Text;
using System.Collections.Generic;
using PhotoTag.Contracts;

namespace PhotoTag.Server.Controllers {
	[ApiController]
	[Route("api/[controller]")]
	//komentář zde
	//komentař dva
	public class ManufacturersController : ControllerBase {
		private static string ManufacturersXmlPath => Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "manufacturers.xml");

		[HttpGet]
		public IActionResult GetAll([FromQuery] string format = "json") {
			if (!System.IO.File.Exists(ManufacturersXmlPath))
				return NotFound("manufacturers.xml not found");

			var xml = System.IO.File.ReadAllText(ManufacturersXmlPath, Encoding.UTF8);

			if (format.ToLower() == "xml") {
				return Content(xml, "application/xml");
			}

			var serializer = new XmlSerializer(typeof(List<Manufacturer>));
			List<Manufacturer> manufacturers;
			using (var stream = System.IO.File.OpenRead(ManufacturersXmlPath)) {
				manufacturers = serializer.Deserialize(stream) as List<Manufacturer>;
			}
			return Ok(manufacturers);
		}
		[HttpGet("image")]
		public IActionResult GetImage([FromQuery] string filename) {
			if (string.IsNullOrWhiteSpace(filename))
				return BadRequest("Filename required.");

			// Strict allow-list: a bare filename (no path separators, no "..", no drive letter)
			// with one of the expected image extensions. This endpoint used to build a path
			// directly from the query string, which let "?filename=../../envsecrets.txt" read
			// the JWT signing secret through this anonymous endpoint - rejecting anything that
			// doesn't match this shape closes that off rather than stripping specific characters.
			if (!System.Text.RegularExpressions.Regex.IsMatch(filename, @"^[A-Za-z0-9_-]+\.(jpg|jpeg|png|gif)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
				return BadRequest("Invalid filename.");

			var baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
			var imagesDir = Path.Combine(baseDir, "ClientHTML", "Images");
			var imagePath = Path.Combine(imagesDir, filename);

			// Belt and suspenders: confirm the resolved path still lands inside the images
			// directory, in case the allow-list above is ever loosened later.
			var fullImagesDir = Path.GetFullPath(imagesDir) + Path.DirectorySeparatorChar;
			var fullImagePath = Path.GetFullPath(imagePath);
			if (!fullImagePath.StartsWith(fullImagesDir, StringComparison.OrdinalIgnoreCase))
				return BadRequest("Invalid filename.");

			if (!System.IO.File.Exists(imagePath))
				return NotFound("Image not found.");

			var ext = Path.GetExtension(filename)?.ToLowerInvariant();
			var contentType = ext switch {
				".jpg" => "image/jpeg",
				".jpeg" => "image/jpeg",
				".png" => "image/png",
				".gif" => "image/gif",
				_ => "application/octet-stream"
			};
			var bytes = System.IO.File.ReadAllBytes(imagePath);
			return File(bytes, contentType);
		}
	}
}
