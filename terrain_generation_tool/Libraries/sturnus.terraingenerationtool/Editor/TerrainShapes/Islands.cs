using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool;
public static class Islands
{
	public static float Default( int x, int y, int width, int height, long seed, float minHeight, bool warp, float warpSize = 0.1f, float warpStrength = 0.5f )
	{
		float nx = (x / (float)width) * 2 - 1; // Normalize x to range [-1, 1]
		float ny = (y / (float)height) * 2 - 1; // Normalize y to range [-1, 1]
		float warpX;
		float warpY;
		float warpedNx;
		float warpedNy;
		float noise;
		if ( warp )
		{
			// Generate warp offsets using additional noise
			warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;

			// Apply domain warping
			warpedNx = nx + warpX;
			warpedNy = ny + warpY;
		}
		else
		{
			warpedNx = nx;
			warpedNy = ny;
		}
		
		// Radial distance from the center
		float distance = (float)Math.Sqrt( nx * nx + ny * ny );
		float falloff = 1.0f - Math.Clamp( distance, 0.25f, 1 ); // Smooth taper from center to edge

		// Central mountain shape (parabolic for smooth curvature)
		float centralMountain = (1.0f - distance * distance) * falloff;

		// Beach-style taper near the edges
		float beachStart = 0.5f; // Start of the beach region (distance normalized)
		float beachEnd = 0.98f;   // End of the beach region (ocean level)
		float beachFalloff = Math.Clamp( (distance - beachStart) / (beachEnd - beachStart), 0.1f, 1 );
		float beachTaper = (1.0f - beachFalloff) * 0.25f; // Smooth transition to flat region

		// Add subtle noise for terrain variation
		if ( warp )
		{
			noise = OpenSimplex2S.Noise2( seed, warpedNx * 2, warpedNy * 2 ) * 0.4f;
		}
		else
		{
			noise = OpenSimplex2S.Noise2( seed, nx * 6, ny * 6 ) * 0.05f; // Low-frequency noise
		}
		
		// Combine components: central mountain, beach taper, and noise
		float output = centralMountain * (1.0f - beachFalloff) + beachTaper + noise;

		// Combine all effects
		float heightValue = output;

		// Add a baseline value to ensure no flat zero areas
		float baseline = minHeight; // Minimum height
		float heightValueCombined = MathF.Max( heightValue, baseline );

		// Clamp the final height to valid range
		return heightValueCombined;
	}

	public static float Archipelagos( int x, int y, int width, int height, long seed, float minHeight, bool warp, float warpSize, float warpStrength )
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float nx = (x / (float)width) * 2 - 1;
		float ny = (y / (float)height) * 2 - 1;

		// Apply domain warping
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Use noise layers to create clusters of small islands
		float baseNoise = OpenSimplex2S.Noise2( seed, nx * 1.5f, ny * 1.5f );
		float secondaryNoise = OpenSimplex2S.Noise2( seed + 1, nx * 3.0f, ny * 3.0f ) * 0.5f;

		float archipelagoHeight = baseNoise + secondaryNoise;

		// Apply radial falloff to form rounded island clusters
		float distance = MathF.Sqrt( nx * nx + ny * ny );
		float falloff = Math.Clamp( 1 - distance * 1.2f, 0, 1 );
		float heightValue = Math.Clamp( archipelagoHeight * falloff, 0, 1 );
		// Add a baseline value to ensure no flat zero areas
		float baseline = minHeight; // Minimum height
		float heightValueCombined = MathF.Max( heightValue, baseline );

		// Clamp the final height to valid range
		return heightValueCombined;
	}

	public static float Atoll( int x, int y, int width, int height, long seed, float minHeight, bool warp, float warpSize, float warpStrength )
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float nx = (x / (float)width) * 2 - 1; // Normalize x to range [-1, 1]
		float ny = (y / (float)height) * 2 - 1; // Normalize y to range [-1, 1]

		// Apply domain warping
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 20, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 21, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Calculate distance from the center
		float distance = MathF.Sqrt( nx * nx + ny * ny );

		// Define parameters for the single ring
		float ringCenter = 0.6f; // Center of the ring
		float ringWidth = 0.1f;  // Width of the ring

		// Create a single ring using a Gaussian-like function
		float ring = MathF.Exp( -MathF.Pow( (distance - ringCenter) / ringWidth, 2 ) );

		// Add some noise for variation
		float baseNoise = OpenSimplex2S.Noise2( seed, nx * 1.0f, ny * 1.0f ) * 0.4f;

		// Introduce a beach-like area (reduce noise and height for one side of the map)
		float beachEffect = Math.Clamp( (1 - nx) * 0.5f, 0.2f, 1.0f ); // Reduces height on one side of the map
		float beachNoise = OpenSimplex2S.Noise2( seed + 30, nx * 2.0f, ny * 2.0f ) * 0.2f;

		// Combine the ring, noise, and beach effect
		float heightValue = (ring + baseNoise * beachEffect + beachNoise) * beachEffect;

		// Add a baseline value to ensure no flat zero areas
		float baseline = minHeight; // Minimum height
		float heightValueCombined = MathF.Max( heightValue, baseline );

		// Clamp the final height to valid range
		return heightValueCombined;
	}




	public static float Islets( int x, int y, int width, int height, long seed, float minHeight, bool warp, float warpSize, float warpStrength )
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float nx = (x / (float)width) * 2 - 1;
		float ny = (y / (float)height) * 2 - 1;

		// Apply domain warping
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 30, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 31, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Generate scattered small islands
		float scatterNoise = OpenSimplex2S.Noise2( seed, nx * 6.0f, ny * 6.0f );
		float baseNoise = OpenSimplex2S.Noise2( seed + 1, nx * 3.0f, ny * 3.0f ) * 0.5f;

		float isletHeight = scatterNoise + baseNoise;

		// Apply distance falloff to create isolated islets
		float distance = MathF.Sqrt( nx * nx + ny * ny );
		float falloff = Math.Clamp( 1 - distance * 1.5f, 0, 1 );
		float heightValue = Math.Clamp( isletHeight * falloff, 0, 1 );
		// Add a baseline value to ensure no flat zero areas
		float baseline = minHeight; // Minimum height
		float heightValueCombined = MathF.Max( heightValue, baseline );

		// Clamp the final height to valid range
		return heightValueCombined;
	}

	public static float Oceanic( int x, int y, int width, int height, long seed, float minHeight, bool warp, float warpSize, float warpStrength )
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float nx = (x / (float)width) * 2 - 1;
		float ny = (y / (float)height) * 2 - 1;

		// Apply domain warping
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 40, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 41, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Generate large, continuous landmass with a few scattered features
		float baseNoise = OpenSimplex2S.Noise2( seed, nx * 1.0f, ny * 1.0f );
		float featureNoise = OpenSimplex2S.Noise2( seed + 1, nx * 2.0f, ny * 2.0f ) * 0.5f;

		float oceanicHeight = baseNoise + featureNoise;

		// Apply radial falloff for a natural ocean/land mix
		float distance = MathF.Sqrt( nx * nx + ny * ny );
		float falloff = Math.Clamp( 1 - distance * 1.0f, 0, 1 );

		float heightValue = Math.Clamp( oceanicHeight * falloff, 0, 1 );

		float baseline = minHeight; // Minimum height
		float heightValueCombined = MathF.Max( heightValue, baseline );

		// Clamp the final height to valid range
		return heightValueCombined;
	}

}
