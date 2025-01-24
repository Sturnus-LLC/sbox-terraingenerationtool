using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool;
public static class Sea
{
	public static float SeaBed(
		int x,
		int y,
		int width,
		int height,
		long seed,
		float minHeight,
		bool domainWarping,
		float domainWarpingSize,
		float domainWarpingStrength
	)
	{
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float depthScale = 0.4f; // Adjust the overall depth
		float waveFrequency = 0.5f; // Frequency of base ripples
		float waveAmplitude = 0.1f; // Height of ripples
		float randomVariation = 0.02f; // Subtle randomness
		float distortionFrequency = 0.03f; // Frequency for distortion
		float distortionStrength = 0.05f ;	// Strength of distortion
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
		
		// Add a baseline value to ensure no flat zero areas
		float baseline = minHeight; // Minimum height
		heightValue = MathF.Max( heightValue, baseline );

		// Scale to depth and clamp
		heightValue = heightValue * depthScale;
		return Math.Clamp( heightValue, 0.0f, 1.0f );
	}

	public static float Cliff(
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
		float hillHeight = 0.9f;    // Height of the cliff
		float slopeWidth = 0.2f;     // Width of the slope transition
		float wideningFactor = 0.9f;
		// Apply domain warping for cliff irregularity
		if ( warp )
		{
			float warpX = OpenSimplex2S.Noise2( seed + 10, nx * warpSize, ny * warpSize ) * warpStrength;
			float warpY = OpenSimplex2S.Noise2( seed + 11, nx * warpSize, ny * warpSize ) * warpStrength;
			nx += warpX;
			ny += warpY;
		}

		// Calculate distance for the hill gradient
		float distance = nx >= 0 ? MathF.Abs( nx ) : MathF.Abs( nx ) * (1 - wideningFactor); // Widen on one side

		// Generate hill gradient using a smooth transition
		float hill = Math.Clamp( 1.0f - MathF.Pow( distance / slopeWidth, 2.0f ), 0, 1 ); // Quadratic falloff for smoother slope
		hill *= hillHeight; // Scale the hill to the desired height

		// Add base noise for texture
		float baseNoise = OpenSimplex2S.Noise2( seed, nx * 6.0f, ny * 6.0f ) * 0.2f;

		// Add finer noise for additional detail
		float fineNoise = OpenSimplex2S.Noise2( seed + 1, nx * 12.0f, ny * 12.0f ) * (0.2f / 2);

		// Combine hill gradient with noise
		float heightValue = hill + baseNoise + fineNoise;

		float baseValue = Math.Max( baseNoise, minHeight );
		heightValue = Math.Max( heightValue, baseValue );

		// Clamp the height value to ensure valid results
		return Math.Clamp( heightValue, 0, 1 );
	}




}
