using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool.Volcanic;
public static class RealisticShapes
{
	public static float DefaultRealistic(
	int x,
	int y,
	int width,
	int height,
	long seed,
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
		float baseline = 0.2f; // Minimum height
		heightValue = MathF.Max( heightValue, baseline );

		// Normalize height to [0, 1]
		return Math.Clamp( heightValue, 0.0f, 1.0f );
	}

	private static float SmoothStep( float edge0, float edge1, float x )
	{
		x = Math.Clamp( (x - edge0) / (edge1 - edge0), 0.0f, 1.0f ); // Normalize to [0, 1]
		return x * x * (3 - 2 * x); // Smoothstep formula
	}

}
