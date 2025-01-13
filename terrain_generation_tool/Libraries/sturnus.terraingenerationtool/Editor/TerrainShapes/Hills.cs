using Editor;
using Sandbox;
using Sandbox.UI;
using System;

namespace Sturnus.TerrainGenerationTool.Hills;
public static class HillsShapes
{
	public static float DefaultHills( int x, int y, int width, int height, long seed, bool warp, float warpSize = 0.1f, float warpStrength = 0.5f )
	{
		float nx = x / (float)width; // Normalize x to range [0, 1]
		float ny = y / (float)height; // Normalize y to range [0, 1]
		float warpX;
		float warpY;
		float warpedNx;
		float warpedNy;
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

		float baseHills = OpenSimplex2S.Noise2( seed, warpedNx * 2, warpedNy * 2 ) * 0.4f;

		// Add low-frequency noise for broad flat areas
		float flatAreas = Math.Abs( OpenSimplex2S.Noise2( seed + 1, nx * 0.5f, ny * 0.5f ) ) * 0.1f;

		// Combine base hills and flat areas
		float combined = baseHills + flatAreas;

		// Add subtle high-frequency noise for variation
		float detail = OpenSimplex2S.Noise2( seed + 2, nx * 8, ny * 8 ) * 0.1f;

		// Final heightmap value
		return Math.Clamp( combined + detail, 0, 1 );
	}
}
