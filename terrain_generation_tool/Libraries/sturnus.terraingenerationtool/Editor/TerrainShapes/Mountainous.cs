using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool;
public static class Mountainous
{
	public static float Default( int x, int y, int width, int height, long seed, float minHeight, bool warp, float warpSize = 0.1f, float warpStrength = 0.5f )
	{
		float nx = x / (float)width; // Normalize x to range [0, 1]
		float ny = y / (float)height; // Normalize y to range [0, 1]
		float warpX;
		float warpY;
		float warpedNx;
		float warpedNy;

		if( warp )
		{
			// Generate warp offsets using noise
			warpX = OpenSimplex2S.Noise2( seed + 20, nx * warpSize, ny * warpSize ) * warpStrength;
			warpY = OpenSimplex2S.Noise2( seed + 21, nx * warpSize, ny * warpSize ) * warpStrength;

			// Apply domain warping to the coordinates
			warpedNx = nx + warpX;
			warpedNy = ny + warpY;
		}
		else
		{
			warpedNx = nx;
			warpedNy = ny;
		}
		

		// Base noise for ridge structure (using warped coordinates)
		float ridgeNoise = Math.Abs( OpenSimplex2S.Noise2( seed, warpedNx * 5, warpedNy * 0.5f ) ) * 0.8f;

		// Add distortion to the ridge line to make it less uniform
		float distortion = OpenSimplex2S.Noise2( seed + 2, warpedNx * 2, warpedNy * 2 ) * 0.3f;
		ridgeNoise += distortion;

		// Add fine detail to the mountains with higher frequency noise
		float detailNoise = OpenSimplex2S.Noise2( seed + 1, warpedNx * 20, warpedNy * 20 ) * 0.2f;

		// Combine ridge, distortion, and detail noise
		float combinedNoise = ridgeNoise + detailNoise;

		// Apply a falloff effect to keep the edges lower
		float edgeFalloff = 1.0f - Math.Clamp( Math.Abs( nx - 0.5f ) + Math.Abs( ny - 0.5f ), 0, 1 );

		// Combine all effects
		float heightValue = combinedNoise * edgeFalloff;

		// Add a baseline value to ensure no flat zero areas
		float baseline = minHeight; // Minimum height
		heightValue = MathF.Max( heightValue, baseline );

		// Combine everything with edge falloff
		return Math.Clamp( heightValue, 0, 1 );
	}
}
