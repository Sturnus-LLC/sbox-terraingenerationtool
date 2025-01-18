using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool.Islands;
public static class IslandShapes
{
	public static float DefaultIsland( int x, int y, int width, int height, long seed, bool warp, float warpSize = 0.1f, float warpStrength = 0.5f )
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

		// Clamp the final height to valid range
		return output;
	}

	public static float IslandShapeDynamic( int x, int y, int width, int height )
	{
		float nx = (x / (float)width) * 2 - 1;
		float ny = (y / (float)height) * 2 - 1;
		float distance = MathF.Sqrt( nx * nx + ny * ny );

		// Gradual transition with a parabolic adjustment
		return Math.Clamp( 1.0f - (distance * distance), 0.25f, 1 );
	}
}
