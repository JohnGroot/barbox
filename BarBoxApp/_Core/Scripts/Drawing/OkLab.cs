using System;
using Godot;

namespace BarBox.Core.Drawing;

/// <summary>
/// OKLab color space (Björn Ottosson, https://bottosson.github.io/posts/oklab/).
///
/// Interpolating in OKLab keeps a blue→yellow ramp saturated where an sRGB lerp passes
/// through gray. Coefficients below are Ottosson's canonical matrices written as explicit dot
/// products. This is NOT a transcription of addons/ShapesRenderer/Shaders/oklab.gdshaderinc:
/// GLSL mat3(a, b, c) builds columns, so that shader's matrices are transposed relative to
/// canonical OKLab, and its oklab_mix() is not a true Lab lerp (it works in cube-root LMS with
/// a mid-gain hump and produces NaN on negative input).
/// </summary>
public static class OkLab
{
	public static Vector3 FromLinear(Vector3 linearRgb)
	{
		float l = (0.4122214708f * linearRgb.X) + (0.5363325363f * linearRgb.Y) + (0.0514459929f * linearRgb.Z);
		float m = (0.2119034982f * linearRgb.X) + (0.6806995451f * linearRgb.Y) + (0.1073969566f * linearRgb.Z);
		float s = (0.0883024619f * linearRgb.X) + (0.2817188376f * linearRgb.Y) + (0.6299787005f * linearRgb.Z);

		// Cbrt is signed by definition, so out-of-gamut negatives stay finite.
		float lRoot = MathF.Cbrt(l);
		float mRoot = MathF.Cbrt(m);
		float sRoot = MathF.Cbrt(s);

		return new Vector3(
			(0.2104542553f * lRoot) + (0.7936177850f * mRoot) - (0.0040720468f * sRoot),
			(1.9779984951f * lRoot) - (2.4285922050f * mRoot) + (0.4505937099f * sRoot),
			(0.0259040371f * lRoot) + (0.7827717662f * mRoot) - (0.8086757660f * sRoot));
	}

	public static Vector3 ToLinear(Vector3 lab)
	{
		float lRoot = lab.X + (0.3963377774f * lab.Y) + (0.2158037573f * lab.Z);
		float mRoot = lab.X - (0.1055613458f * lab.Y) - (0.0638541728f * lab.Z);
		float sRoot = lab.X - (0.0894841775f * lab.Y) - (1.2914855480f * lab.Z);

		float l = lRoot * lRoot * lRoot;
		float m = mRoot * mRoot * mRoot;
		float s = sRoot * sRoot * sRoot;

		return new Vector3(
			(4.0767416621f * l) - (3.3077115913f * m) + (0.2309699292f * s),
			(-1.2684380046f * l) + (2.6097574011f * m) - (0.3413193965f * s),
			(-0.0041960863f * l) - (0.7034186147f * m) + (1.7076147010f * s));
	}

	/// <summary>
	/// 2D vertex colors are consumed sRGB-encoded (hdr_2d off) but OKLab is defined on linear
	/// RGB, so every entry point crosses the boundary explicitly.
	/// </summary>
	public static Vector3 FromSrgb(Color srgb)
	{
		Color linear = srgb.SrgbToLinear();
		return FromLinear(new Vector3(linear.R, linear.G, linear.B));
	}

	public static Color ToSrgb(Vector3 lab, float alpha)
	{
		Vector3 linear = ToLinear(lab);
		return new Color(linear.X, linear.Y, linear.Z, alpha).LinearToSrgb();
	}

	/// <summary>Interpolates in OKLab. Alpha lerps linearly; endpoints are returned exactly.</summary>
	public static Color Mix(Color a, Color b, float t)
	{
		if (t <= 0f)
		{
			return a;
		}

		if (t >= 1f)
		{
			return b;
		}

		Vector3 labA = FromSrgb(a);
		Vector3 labB = FromSrgb(b);
		float alpha = Mathf.Lerp(a.A, b.A, t);

		return ToSrgb(labA.Lerp(labB, t), alpha);
	}

	/// <summary>
	/// Samples a T-sorted stop array at normalized arc length. Clamps outside the stop range.
	/// Returns stop colors exactly at their own T rather than round-tripping through OKLab,
	/// which is only accurate to ~1e-3 and would visibly drift the authored endpoints.
	/// </summary>
	public static Color Sample(ColorStop[] stops, float t)
	{
		if (stops == null || stops.Length == 0)
		{
			return Colors.Magenta;
		}

		if (stops.Length == 1 || t <= stops[0].T)
		{
			return stops[0].Color;
		}

		int last = stops.Length - 1;
		if (t >= stops[last].T)
		{
			return stops[last].Color;
		}

		for (int i = 0; i < last; i++)
		{
			ColorStop lo = stops[i];
			ColorStop hi = stops[i + 1];
			if (t > hi.T)
			{
				continue;
			}

			float span = hi.T - lo.T;
			if (span <= 0f)
			{
				return hi.Color;
			}

			return Mix(lo.Color, hi.Color, (t - lo.T) / span);
		}

		return stops[last].Color;
	}
}
