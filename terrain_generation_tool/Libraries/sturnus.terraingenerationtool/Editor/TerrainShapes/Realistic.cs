using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool;
public static class Realistic
{
	public static float Default(
	int x,
	int y,
	int width,
	int height,
	long seed,
	float minHeight,
	bool domainWarping,
	float domainWarpingSize,
	float domainWarpingStrength)
	{
		// Normalize coordinates to [-1, 1]
		float nx = (x / (float)width) * 2 - 1;
		float ny = (y / (float)height) * 2 - 1;

		// Apply domain warping for natural distortion
		if ( domainWarping )
		{
			float warpX = OpenSimplex2S.Noise2( seed, nx * domainWarpingSize, ny * domainWarpingSize ) * domainWarpingStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 1, nx * domainWarpingSize, ny * domainWarpingSize ) * domainWarpingStrength;
			nx += warpX;
			ny += warpY;
		}

		float baseTerrain = OpenSimplex2S.Noise2( seed, nx, ny );

		// Create hill/valley transitions with non-linear blending
		float hillFactor = MathF.Pow( baseTerrain, 6 ); // Emphasize hills
		float valleyFactor = 1.0f - MathF.Pow( 1.0f - baseTerrain, 1 ); // Emphasize valleys

		// Use smoothstep-like function for non-linear blending
		float smoothTransition = SmoothStep( 0.75f, 1.25f, baseTerrain );
		float terrainShape = MathX.Lerp( valleyFactor, hillFactor, smoothTransition );

		// Add finer details
		float fineNoise = OpenSimplex2S.Noise2( seed + 2, nx * 8.0f, ny * 8.0f ) * 0.1f;

		// Combine base terrain with fine details
		float heightValue = terrainShape + fineNoise;

		// Add a baseline value to ensure no flat zero areas
		float baseline = minHeight; // Minimum height
		heightValue = MathF.Max( heightValue, baseline );

		// Normalize height to [0, 1]
		return Math.Clamp( heightValue, 0.0f, 1.0f );
	}

	public static float Hills( int x, int y, int width, int height, long seed, float minHeight, bool warp, float warpSize = 0.1f, float warpStrength = 0.5f )
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float nx = (x / (float)width) * 2 - 1; // Normalize x to range [-1, 1]
		float ny = (y / (float)height) * 2 - 1; // Normalize y to range [-1, 1]

		float hillHeight = 0.6f;   // Maximum height of the hills
		float hillFrequency = 3f; // Frequency of the hills
		float noiseStrength = 0f; // Strength of noise for natural detail

		// Apply domain warping for irregularity
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Base overlapping hills
		float hillBase1 = MathF.Sin( nx * hillFrequency * MathF.PI ) + MathF.Cos( ny * hillFrequency * MathF.PI );
		float hillBase2 = MathF.Sin( ny * (hillFrequency * 0.75f) * MathF.PI ) + MathF.Cos( nx * (hillFrequency * 0.75f) * MathF.PI );
		float combinedHills = (hillBase1 + hillBase2) / 5f; // Blend two hill patterns
		combinedHills = MathF.Abs( combinedHills ); // Ensure positive-only values

		// Add noise for natural detail
		float baseNoise = OpenSimplex2S.Noise2( seed, nx * 6.0f, ny * 6.0f ) * noiseStrength;
		float fineNoise = OpenSimplex2S.Noise2( seed + 1, nx * 12.0f, ny * 12.0f ) * (noiseStrength / 2);

		// Combine hills and noise
		float heightValue = (combinedHills * hillHeight) + minHeight + fineNoise;
		// Ensure minimum base height
	
		// Clamp the final height value
		return Math.Clamp( heightValue, 0, 1 );
	}

	public static float Plateau(
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

		float plateauHeight = 0.8f;   // Maximum height of the plateau
		float widthRatio = 0.75f;     // Width of the side that does not extend to the edge
		float slopeWidthRatio = 0.02f; // Width of the slope transition
		float slopeNoiseStrength = 0.1f; // Strength of additional noise on slopes
		float baseHeight = 0.1f;     // Minimum terrain height
		float noiseStrength = 0f;  // Strength of noise for natural variation
		float cutInOutStrength = 2f; // Strength of the cut-ins and jut-outs
		float topNoiseStrength = 0f; // Reduced noise strength for the plateau to

		// Apply domain warping for irregularity
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Define the plateau boundaries
		float plateauStartX = -1f; // Left edge
		float plateauEndX = widthRatio * 2 - 1; // End of the one side
		float plateauStartY = -1f; // Bottom edge
		float plateauEndY = 1f; // Top edge (extends to the edge)

		// Check if the current point is within the flat plateau region
		bool isFlatPlateau = nx >= plateauStartX && nx <= plateauEndX && ny >= plateauStartY && ny <= plateauEndY;

		// Flat plateau height
		float heightValue = isFlatPlateau ? plateauHeight : 0f;

		// Add slopes with jut-outs and cut-ins for smooth, natural drop-offs
		if ( !isFlatPlateau )
		{
			// Add noise-based cut-ins and jut-outs
			float noiseCut = OpenSimplex2S.Noise2( seed + 20, nx * 10.0f, ny * 10.0f ) * cutInOutStrength;
			// Generate additional noise for slopes
			float slopeNoise = OpenSimplex2S.Noise2( seed + 30, nx * 15.0f, ny * 15.0f ) * slopeNoiseStrength;

			// Left slope
			if ( nx < plateauStartX )
			{
				float slope = Math.Clamp( 1f - MathF.Abs( (nx - plateauStartX) / slopeWidthRatio ) + noiseCut  + slopeNoise, 0f, 1f );
				heightValue = Math.Max( heightValue, slope * plateauHeight );
			}
			// Right slope (for the single side ending early)
			else if ( nx > plateauEndX )
			{
				float slope = Math.Clamp( 1f - MathF.Abs( (nx - plateauEndX) / slopeWidthRatio ) + noiseCut + slopeNoise, 0f, 1f );
				heightValue = Math.Max( heightValue, slope * plateauHeight );
			}
			// Bottom slope
			if ( ny < plateauStartY )
			{
				float slope = Math.Clamp( 1f - MathF.Abs( (ny - plateauStartY) / slopeWidthRatio ) + noiseCut + slopeNoise, 0f, 1f );
				heightValue = Math.Max( heightValue, slope * plateauHeight );
			}
			// Top slope
			if ( ny > plateauEndY )
			{
				float slope = Math.Clamp( 1f - MathF.Abs( (ny - plateauEndY) / slopeWidthRatio ) + noiseCut + slopeNoise, 0f, 1f );
				heightValue = Math.Max( heightValue, slope * plateauHeight );
			}
		}

		// Add noise for terrain variation
		float baseNoise = OpenSimplex2S.Noise2( seed + 100, nx * 8.0f, ny * 8.0f ) * topNoiseStrength;

		float fineNoise = OpenSimplex2S.Noise2( seed + 1, nx * 16.0f, ny * 16.0f ) * (noiseStrength / 2);

		// Add the noise and base height to the terrain
		heightValue += baseNoise + fineNoise + baseHeight;

		// Ensure the base terrain height does not fall below baseHeight
		heightValue = Math.Max( heightValue, baseHeight );

		var heightValueBase = Math.Max( heightValue, minHeight );

		// Clamp the final height
		return Math.Clamp( heightValueBase, 0, 1 );
	}

	private static float SmoothStep( float edge0, float edge1, float x )
	{
		x = Math.Clamp( (x - edge0) / (edge1 - edge0), 0.0f, 1.0f ); // Normalize to [0, 1]
		return x * x * (3 - 2 * x); // Smoothstep formula
	}

}
