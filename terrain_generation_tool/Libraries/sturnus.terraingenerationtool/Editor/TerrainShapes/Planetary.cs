using Editor;
using Sandbox;
using System;
using System.Collections.Generic;

namespace Sturnus.TerrainGenerationTool;
public static class Planetary
{
	public static float Sharded(
		int x,
		int y,
		int width,
		int height,
		long seed,
		float minHeight,
		bool warp,              // Apply domain warping
		float warpSize,         // Warp scale
		float warpStrength      // Warp strength
	)
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float nx = (x / (float)width) * 2 - 1; // Normalize x to range [-1, 1]
		float ny = (y / (float)height) * 2 - 1; // Normalize y to range [-1, 1]

		float shardHeight = 10f;
		float crackDepth = 1f;
		float noiseStrength = 0.05f;
		int cellCount = 5;

		// Apply domain warping for irregularity
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Generate cracks
		float minDist = float.MaxValue;
		float secondaryDist = float.MaxValue;
		float cellSize = 2.0f / cellCount; // Normalize the cell size
		for ( int i = 0; i < cellCount; i++ )
		{
			for ( int j = 0; j < cellCount; j++ )
			{
				float cellX = -1 + i * cellSize + OpenSimplex2S.Noise2( seed + 20, i, j ) * cellSize * 0.5f;
				float cellY = -1 + j * cellSize + OpenSimplex2S.Noise2( seed + 21, i, j ) * cellSize * 0.5f;

				float distance = MathF.Sqrt( (nx - cellX) * (nx - cellX) + (ny - cellY) * (ny - cellY) );

				if ( distance < minDist )
				{
					secondaryDist = minDist;
					minDist = distance;
				}
				else if ( distance < secondaryDist )
				{
					secondaryDist = distance;
				}
			}
		}

		// Compute shard height based on the secondary distance
		float shardNoise = OpenSimplex2S.Noise2( seed + 30, nx, ny ) * noiseStrength;
		float heightValue = MathF.Max( secondaryDist - minDist, 0f ) * shardHeight + shardNoise;

		// Apply crack depth at borders between shards
		if ( secondaryDist - minDist < 0.03f ) // Control the width of cracks
		{
			heightValue -= crackDepth;
		}

		// Add a baseline value to ensure no flat zero areas
		//heightValue = MathF.Max( heightValue, minHeight );

		// Clamp height to avoid negative values
		heightValue = Math.Clamp( heightValue, 0, 1 );

		float baseline = minHeight; // Minimum height
		float heightValueCombined = MathF.Max( heightValue, baseline );

		// Clamp the final height to valid range
		return heightValueCombined;
	}

	public static float Craters(
		int x,
		int y,
		int width,
		int height,
		long seed,
		float minHeight,
		bool warp,             // Apply domain warping for irregularity
		float warpSize,        // Warp scale
		float warpStrength     // Warp strength
	)
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float nx = (x / (float)width) * 2 - 1; // Normalize x to range [-1, 1]
		float ny = (y / (float)height) * 2 - 1; // Normalize y to range [-1, 1]

		int craterCount = 100;
		float minCraterSize = 0.1f;
        float maxCraterSize = 0.3f;
        float craterDepth = 0.05f;
        float rimHeight = 0.05f;
        float rimWidthRatio = 0.2f;
        float noiseStrength = 0.05f;
        float largeCraterRatio = 0.2f;
		float slopeFalloff = 0.9f;  // Controls smoothness of ramps


		// Apply domain warping for irregularity
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Base terrain noise
		float baseTerrain = OpenSimplex2S.Noise2( seed + 1, nx * 2.0f, ny * 2.0f ) * noiseStrength;
		baseTerrain = (baseTerrain + 1) * 0.5f; // Normalize to [0, 1]

		float heightValue = baseTerrain;

		// Iterate through craters in reverse order to overwrite previous craters
		for ( int i = craterCount - 1; i >= 0; i-- )
		{
			// Randomize crater properties
			float craterX = random.Next( -100000, 100000 ) / 50000.0f;
			float craterY = random.Next( -100000, 100000 ) / 50000.0f;
			float craterRadius = (i < craterCount * largeCraterRatio)
				? random.Next( (int)(maxCraterSize * 500), (int)(maxCraterSize * 1000) ) / 1000.0f // Large craters
				: random.Next( (int)(minCraterSize * 500), (int)(minCraterSize * 1000) ) / 1000.0f; // Small craters

			// Distance from the current point to the crater center
			float distance = MathF.Sqrt( (nx - craterX) * (nx - craterX) + (ny - craterY) * (ny - craterY) );

			if ( distance < craterRadius )
			{
				float rimStart = craterRadius * (1f - rimWidthRatio);
				float rimEnd = craterRadius;

				// Inside the pit
				if ( distance < rimStart )
				{
					float pitFalloff = Math.Clamp( 1f - (distance / rimStart), 0f, 1f );
					heightValue = baseTerrain - MathF.Pow( pitFalloff, slopeFalloff ) * craterDepth; // Smooth ramp to the center
				}
				// Raised rim
				else if ( distance >= rimStart && distance < rimEnd )
				{
					float rimFalloff = Math.Clamp( (distance - rimStart) / (rimEnd - rimStart), 0f, 1f );
					heightValue = baseTerrain + MathF.Pow( 1f - rimFalloff, slopeFalloff ) * rimHeight; // Rounded rim
				}

				// Reset terrain below the rim to prevent intersecting ridges
				if ( distance >= rimEnd )
				{
					heightValue = baseTerrain;
				}

				// Exit the loop once the current crater is applied
				break;
			}
		}

		// Ensure height values are clamped to [0, 1]
		heightValue = Math.Clamp( heightValue, 0, 1 );

		return heightValue;
	}


}
