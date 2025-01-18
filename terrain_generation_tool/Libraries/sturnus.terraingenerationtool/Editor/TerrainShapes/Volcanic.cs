using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool.Volcanic;
public static class VolcanicShapes
{
	public static float DefaultVolcanic(
	int x,
	int y,
	int width,
	int height,
	long seed,
	bool domainWarping,
	float domainWarpingSize,
	float domainWarpingStrength
)
	{
		// Normalize coordinates to [-1, 1]
		float nx = (x / (float)width) * 2 - 1;
		float ny = (y / (float)height) * 2 - 1;

		// Calculate distance from the center
		float distance = MathF.Sqrt( nx * nx + ny * ny );

		// Base shape for the volcano (parabolic crater shape)
		float craterShape = Math.Clamp( 1.0f - MathF.Pow( distance, 1.5f ), 0, 1 );

		// Apply domain warping for additional detail
		if ( domainWarping )
		{
			float warpX = OpenSimplex2S.Noise2( seed, nx * domainWarpingSize, ny * domainWarpingSize ) * domainWarpingStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 1, nx * domainWarpingSize, ny * domainWarpingSize ) * domainWarpingStrength;
			nx += warpX;
			ny += warpY;
		}

		// Add noise for surface details
		float baseNoise = OpenSimplex2S.Noise2( seed, nx * 2.0f, ny * 2.0f ) * 0.5f + 0.5f; // Normalize to [0, 1]
		float fineNoise = OpenSimplex2S.Noise2( seed + 1, nx * 8.0f, ny * 8.0f ) * 0.25f + 0.25f; // Subtle fine details
		float combinedNoise = (baseNoise + fineNoise) / 2.0f; // Average the noise layers

		// Apply crater shape and normalize
		float heightValue = craterShape * combinedNoise;

		return Math.Clamp( heightValue, 0.01f, 1.0f ); // Ensure the height stays within [0, 1]
	}

}
