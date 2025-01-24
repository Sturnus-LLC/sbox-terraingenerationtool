using Editor;
using Sandbox;
using Sandbox.UI;
using System;

namespace Sturnus.TerrainGenerationTool;
public static class Volcanic
{
	public static float Default(
		int x,
		int y,
		int width,
		int height,
		long seed,
		float minHeight,
		bool warp,
		float warpSize,
		float warpStrength
	)
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float nx = (x / (float)width) * 2 - 1; // Normalize x to range [-1, 1]
		float ny = (y / (float)height) * 2 - 1; // Normalize y to range [-1, 1]

		float craterRadius = 0.3f;        // Average radius of the central crater
		float rimHeight = 0.25f;          // Height of the crater rim
		float rimWidth = 0f;           // Width of the rim
		float outerSlopeStrength = 0.5f;  // Strength of the gradient for the exterior slope
		float innerSlopeStrength = 2.0f;  // Strength of the gradient for the interior slope
		float baseHeight = 0.1f;          // Minimum base height
		float noiseStrength = 0.02f; // General noise strength
		float distance = MathF.Sqrt( nx * nx + ny * ny );

		// Calculate distance from the center of the heightmap
		float centerX = 0f, centerY = 0f; // Center of the heightmap
		float distanceToCenter = MathF.Sqrt( (nx - centerX) * (nx - centerX) + (ny - centerY) * (ny - centerY) );

		// Apply domain warping for irregularity
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Introduce irregularity to the crater radius
		float craterIrregularity = OpenSimplex2S.Noise2( seed + 20, nx * 4.0f, ny * 4.0f ) * 0.1f;
		float dynamicCraterRadius = craterRadius + craterIrregularity;

		// Initialize height components
		float crater = 0f;
		float rim = 0f;
		float outerSlope = 0f;

		// Crater: Smooth inward slope towards the center of the crater
		if ( distanceToCenter < dynamicCraterRadius )
		{
			float craterDepth = (1f - (distanceToCenter));
			crater = MathF.Pow( craterDepth, innerSlopeStrength ) ; // Inward slope
		}

		// Rim: Uneven ridge around the crater
		if ( distanceToCenter >= dynamicCraterRadius && distanceToCenter < dynamicCraterRadius + rimWidth )
		{
			float rimFalloff = (distanceToCenter - dynamicCraterRadius) / rimWidth;
			float rimNoise = OpenSimplex2S.Noise2( seed + 30, nx * 8.0f, ny * 8.0f ) * noiseStrength;
			rim = (1f - rimFalloff) * rimHeight + rimNoise; // Add noise for unevenness
		}

		// Outer slope: Smooth gradient with noise towards the edges
		if ( distanceToCenter >= dynamicCraterRadius + rimWidth )
		{
			float slopeDistance = 1f - distanceToCenter; // Decrease height as we approach the edge
			float slopeNoise = OpenSimplex2S.Noise2( seed + 40, nx * 4.0f, ny * 4.0f ) * noiseStrength;
			outerSlope = MathF.Max( 0, slopeDistance ) * outerSlopeStrength + slopeNoise;
		}

		// Combine components
		float heightValue = baseHeight + crater + rim + outerSlope;

		// Smooth transition towards the crater center for a more natural look
		if ( distanceToCenter < dynamicCraterRadius )
		{
			float centerFalloff = MathF.Pow( 1f - (distanceToCenter / dynamicCraterRadius), 2f );
			heightValue = centerFalloff * baseHeight; // Slight bump for a smoother slope
		}


		heightValue = Math.Max( heightValue, baseHeight );

		var heightValueBase = Math.Max( heightValue, minHeight );

		// Clamp the final height
		return Math.Clamp( heightValueBase, 0, 1 );
	}

}
