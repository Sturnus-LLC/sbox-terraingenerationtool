using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool.Volcanic;
public static class VolcanicShapes
{
	public static float DefaultVolcanic( int x, int y, int width, int height, long seed, bool warp, float warpSize = 0.1f, float warpStrength = 0.5f )
	{
		float nx = (x / (float)width) * 2 - 1; // Normalize x to range [-1, 1]
		float ny = (y / (float)height) * 2 - 1; // Normalize y to range [-1, 1]
		float distanceFromCenter = (float)Math.Sqrt( nx * nx + ny * ny ); // Distance from center
		float warpX;
		float warpY;
		float warpedNx;
		float warpedNy;

		if ( warp )
		{
			// Generate warp offsets using noise
			warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;

			// Apply domain warping to the coordinates
			warpedNx = nx + warpX;
			warpedNy = ny + warpY;
		}
		else
		{
			warpedNx = nx;
			warpedNy = ny;
		}

		// Base shape for the volcano with a central peak
		float peak = 1.0f - Math.Clamp( distanceFromCenter * 1.5f, 0, 1 );

		// Add a smoother crater at the top
		float craterRadius = 0.3f; // Size of the crater
		float craterDepth = 0.95f; // Depth of the crater
		if ( distanceFromCenter < craterRadius )
		{
			float craterEffect = 1.0f - (distanceFromCenter / craterRadius);
			peak -= craterEffect * craterDepth;
		}

		// Add roughness to the volcano surface
		float roughness = OpenSimplex2S.Noise2( seed, warpedNx * 6, warpedNy * 6 ) * 0.1f;

		// Add subtle details to the terrain
		float detail = OpenSimplex2S.Noise2( seed + 1, warpedNx * 20, warpedNy * 20 ) * 0.05f;

		// Additional noise outward from the base
		float baseNoise = OpenSimplex2S.Noise2( seed + 2, warpedNx * 3, warpedNy * 3 ) * Math.Clamp( distanceFromCenter - 0.3f, 0, 1 ) * 0.75f;

		// Combine base shape, roughness, detail, and outward noise
		float combined = peak + roughness + detail + baseNoise;

		// Apply a global falloff to taper the terrain near the edges
		float edgeFalloff = 0.8f - Math.Clamp( distanceFromCenter, 0, 1 );

		return Math.Clamp( combined * edgeFalloff, 0, 1 );
	}

}
