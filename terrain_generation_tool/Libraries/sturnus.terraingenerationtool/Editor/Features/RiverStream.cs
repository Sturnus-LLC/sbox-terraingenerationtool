using Editor;
using Sandbox;
using System;

namespace Sturnus.TerrainGenerationTool.RiverStream;
public static class RiverStream
{
	public static float[,] AddRiversAndStreams(
	float[,] heightmap,
	int frequency, // Number of rivers/streams
	float widthScale, // Relative width of rivers/streams
	long seed
)
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );
		float[,] modifiedHeightmap = (float[,])heightmap.Clone();
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );

		// Generate river starting points based on frequency
		for ( int i = 0; i < frequency; i++ )
		{
			int startX = random.Next( 0, width );
			int startY = random.Next( 0, height );

			// Ensure the river starts at a relatively high elevation
			while ( modifiedHeightmap[startX, startY] < 0.5f )
			{
				startX = random.Next( 0, width );
				startY = random.Next( 0, height );
			}

			// Trace the river path
			AddRiverPath( modifiedHeightmap, startX, startY, width, height, widthScale, random );
		}

		return modifiedHeightmap;
	}

	private static void AddRiverPath(
		float[,] heightmap,
		int startX,
		int startY,
		int width,
		int height,
		float widthScale,
		Random random
	)
	{
		int currentX = startX;
		int currentY = startY;

		// Determine the river width based on widthScale
		int riverWidth = Math.Max( 1, (int)(widthScale * width) );

		for ( int steps = 0; steps < width * 2; steps++ ) // Ensure rivers stretch long distances
		{
			// Lower the terrain at the current position to form a river bed
			CarveRiverAtPosition( heightmap, currentX, currentY, riverWidth, width, height );

			// Find the next position by prioritizing downhill movement
			(int nextX, int nextY) = FindNextRiverPosition( heightmap, currentX, currentY, width, height, random );

			// Stop if the river can no longer flow
			if ( nextX == currentX && nextY == currentY )
				break;

			currentX = nextX;
			currentY = nextY;
		}
	}

	private static (int, int) FindNextRiverPosition(
		float[,] heightmap,
		int x,
		int y,
		int width,
		int height,
		Random random
	)
	{
		float currentHeight = heightmap[x, y];
		int nextX = x;
		int nextY = y;
		float lowestHeight = currentHeight;

		// Check all 8 neighbors to find the steepest downhill path
		for ( int offsetY = -1; offsetY <= 1; offsetY++ )
		{
			for ( int offsetX = -1; offsetX <= 1; offsetX++ )
			{
				int nx = x + offsetX;
				int ny = y + offsetY;

				// Skip out-of-bounds and current position
				if ( nx < 0 || nx >= width || ny < 0 || ny >= height || (nx == x && ny == y) )
					continue;

				float neighborHeight = heightmap[nx, ny];
				if ( neighborHeight < lowestHeight )
				{
					lowestHeight = neighborHeight;
					nextX = nx;
					nextY = ny;
				}
			}
		}

		// Add slight randomness to avoid perfectly straight rivers
		if ( random.NextDouble() < 0.3 ) // 30% chance to adjust path
		{
			nextX = Math.Clamp( nextX + random.Next( -1, 2 ), 0, width - 1 );
			nextY = Math.Clamp( nextY + random.Next( -1, 2 ), 0, height - 1 );
		}

		return (nextX, nextY);
	}

	private static void CarveRiverAtPosition(
		float[,] heightmap,
		int x,
		int y,
		int riverWidth,
		int width,
		int height
	)
	{
		for ( int offsetY = -riverWidth / 2; offsetY <= riverWidth / 2; offsetY++ )
		{
			for ( int offsetX = -riverWidth / 2; offsetX <= riverWidth / 2; offsetX++ )
			{
				int nx = x + offsetX;
				int ny = y + offsetY;

				// Ensure we're within bounds
				if ( nx >= 0 && nx < width && ny >= 0 && ny < height )
				{
					// Lower the terrain for the river bed
					float distance = MathF.Sqrt( offsetX * offsetX + offsetY * offsetY );
					float factor = Math.Clamp( 1.0f - (distance / (riverWidth / 2.0f)), 0.0f, 1.0f );
					heightmap[nx, ny] -= factor * 0.03f; // Adjust depth for river carving
				}
			}
		}
	}




}
