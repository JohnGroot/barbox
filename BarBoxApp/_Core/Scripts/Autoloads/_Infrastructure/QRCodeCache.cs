using Godot;
using QRCoder;
using System;
using System.Collections.Generic;

namespace BarBox.Core.Autoloads;

/// <summary>
/// Service for generating and caching QR code textures from URLs.
/// Uses QRCoder library for client-side QR generation.
/// Implements LRU eviction to prevent unbounded memory growth.
/// Thread-safe for concurrent access from multiple callers.
/// </summary>
public class QRCodeCache
{
	private const int QR_MODULE_SIZE = 10;
	private const int QR_QUIET_ZONE = 4;
	private const int MAX_CACHE_SIZE = 20;

	private readonly record struct CacheEntry(ImageTexture Texture, DateTime CreatedAt);
	private readonly Dictionary<string, CacheEntry> _cache = new();
	private readonly object _cacheLock = new();

	/// <summary>
	/// Generate a QR code texture for the given URL.
	/// Results are cached for retry scenarios.
	/// Thread-safe: uses lock for concurrent access.
	/// </summary>
	public ImageTexture GetOrCreateQRCode(string url)
	{
		if (string.IsNullOrEmpty(url))
		{
			GD.PrintErr("[QRCodeCache] Cannot generate QR code for empty URL");
			return null;
		}

		lock (_cacheLock)
		{
			if (_cache.TryGetValue(url, out var cached))
			{
				return cached.Texture;
			}

			// Evict oldest entry if at capacity
			if (_cache.Count >= MAX_CACHE_SIZE)
			{
				EvictOldestEntry();
			}

			var texture = GenerateQRCodeTexture(url);
			if (texture != null)
			{
				_cache[url] = new CacheEntry(texture, DateTime.UtcNow);
			}

			return texture;
		}
	}

	private void EvictOldestEntry()
	{
		string oldestUrl = null;
		var oldestTime = DateTime.MaxValue;

		foreach (var kvp in _cache)
		{
			if (kvp.Value.CreatedAt < oldestTime)
			{
				oldestTime = kvp.Value.CreatedAt;
				oldestUrl = kvp.Key;
			}
		}

		if (oldestUrl != null)
		{
			var entry = _cache[oldestUrl];
			entry.Texture?.Dispose();
			_cache.Remove(oldestUrl);
			GD.Print($"[QRCodeCache] Evicted oldest entry (capacity: {MAX_CACHE_SIZE})");
		}
	}

	/// <summary>
	/// Clear all cached QR codes. Call after payment attempt completes.
	/// Thread-safe: uses lock for concurrent access.
	/// </summary>
	public void ClearCache()
	{
		lock (_cacheLock)
		{
			foreach (var entry in _cache.Values)
			{
				entry.Texture?.Dispose();
			}
			_cache.Clear();
			GD.Print("[QRCodeCache] Cache cleared");
		}
	}

	/// <summary>
	/// Clear a specific URL from cache.
	/// Thread-safe: uses lock for concurrent access.
	/// </summary>
	public void ClearUrl(string url)
	{
		lock (_cacheLock)
		{
			if (_cache.TryGetValue(url, out var entry))
			{
				entry.Texture?.Dispose();
				_cache.Remove(url);
				GD.Print($"[QRCodeCache] Cleared cache for URL: {url}");
			}
		}
	}

	private ImageTexture GenerateQRCodeTexture(string url)
	{
		try
		{
			using var qrGenerator = new QRCodeGenerator();
			using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
			using var qrCode = new PngByteQRCode(qrCodeData);

			// Generate PNG bytes
			var pngBytes = qrCode.GetGraphic(QR_MODULE_SIZE);

			// Create Godot Image from PNG bytes
			var image = new Image();
			var error = image.LoadPngFromBuffer(pngBytes);

			if (error != Error.Ok)
			{
				GD.PrintErr($"[QRCodeCache] Failed to load PNG: {error}");
				return null;
			}

			// Create texture from image
			var texture = ImageTexture.CreateFromImage(image);
			GD.Print($"[QRCodeCache] Generated QR code {image.GetWidth()}x{image.GetHeight()} for URL length {url.Length}");

			return texture;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[QRCodeCache] Failed to generate QR code: {ex.Message}");
			return null;
		}
	}
}
