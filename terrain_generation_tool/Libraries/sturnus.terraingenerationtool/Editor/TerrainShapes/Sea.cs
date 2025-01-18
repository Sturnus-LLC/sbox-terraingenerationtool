using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool.Sea;
public static class SeaShapes
{
	public static float SeaBed(
		int x,
		int y,
		int width,
		int height,
		long seed,
		float depthScale, // Controls overall depth of the sea bed
		float waveFrequency, // Frequency of the ripples
		float waveAmplitude, // Height of the ripples
		float randomVariation, // Adds randomness to the ripples
		float distortionFrequency, // Frequency for distortion
		float distortionStrength // Strength of distortion
	)
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );

		// Normalize coordinates to [0, 1]
		float nx = x / (float)width;
		float ny = y / (float)height;

		// Generate base ripple effect
		float baseRipple = MathF.Sin( nx * waveFrequency * MathF.PI * 2 ) * waveAmplitude
						 + MathF.Sin( ny * waveFrequency * MathF.PI * 2 ) * waveAmplitude;

		// Add distortion to break uniformity
		float distortion = OpenSimplex2S.Noise2( seed + 1, nx * distortionFrequency, ny * distortionFrequency )
						   * distortionStrength;

		// Add random noise for natural variation
		float randomNoise = (float)(random.NextDouble() - 0.5) * randomVariation;

		// Combine all effects
		float heightValue = baseRipple + distortion + randomNoise;

		// Scale to depth and clamp
		heightValue = heightValue * depthScale;
		return Math.Clamp( heightValue, 0.0f, 1.0f );
	}


}
