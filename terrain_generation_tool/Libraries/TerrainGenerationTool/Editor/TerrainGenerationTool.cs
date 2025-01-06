using System;
using System.IO;
using Editor;
using Sandbox;
using SkiaSharp;

[Dock( "Editor", "Terrain Generation Tool", "terrain" )]
public class TerrainGenerationTool : Widget
{
	public string OutputPath { get; set; } = Project.Current.RootDirectory + "\\Assets\\";

	public enum TerrainDimensions : int
	{
		x512 = 512,
		x1024 = 1024,
		x2048 = 2048,
		x4192 = 4192
	}
	public TerrainDimensions TerrainDimensionsEnum { get; set; } = TerrainDimensions.x512;
	[Range( 0.1f, 1f, 0.01f, true, true )] public float TerrainMaxHeight { get; set; } = 0.5f;
	public int TerrainSeed { get; set; } = 1234567890;
	[Range( 1, 20, 1, true, true )] public int SmoothingPasses { get; set; } = 10;
	SKColor[] _splatcolors = { SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Purple, SKColors.Pink, SKColors.Yellow, SKColors.White, SKColors.Brown, SKColors.Silver };
	public float[] _splatthresholds = { 0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f };
	public TerrainGenerationTool( Widget parent ) : base( parent, false )
	{
		Size = new Vector2( 250, 250 );
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
		var DimensionsLabel = Layout.Add( new Label( "Terrain Dimensions" ) );
		var DimensionsEnum = Layout.Add( new EnumControlWidget( this.GetSerialized().GetProperty( nameof( TerrainDimensionsEnum ) ) ) );
		Layout.AddSpacingCell( 5 );
		var MaxHeightLabel = Layout.Add( new Label( "Max Height (relative)" ) );
		var MaxHeightFloat = Layout.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( TerrainMaxHeight ) ) ) );
		Layout.AddSpacingCell( 5 );
		var TerrainSeedLabel = Layout.Add( new Label( "Terrain Seed" ) );
		var TerrainSeedNumber = Layout.Add( new IntegerControlWidget( this.GetSerialized().GetProperty( nameof( TerrainSeed ) ) ) );
		Layout.AddSpacingCell( 5 );
		var SmoothingPassesLabel = Layout.Add( new Label( "Smoothing Passes" ) );
		var SmoothingPassesNumber = Layout.Add( new IntegerControlWidget( this.GetSerialized().GetProperty( nameof( SmoothingPasses ) ) ) );
		Layout.AddSpacingCell( 5 );

		var GenerateButton = Layout.Add( new Button.Primary( "Generate", "auto_awesome", this ) );
		var ExportButton = Layout.Add( new Button( "Export", "file_download", this ) );
		var ApplyTerrainButton = Layout.Add( new Button( "Apply to Terrain", "terrain", this ) );
		//var PreviewLabel = Layout.Add( new Label( "Preview" ) ); Will attempt to get this working in a future update.

		Layout.AddStretchCell();

		GenerateButton.Clicked += () =>
		{
			Log.Info( "Generate!" );
			float[,] heightmap = GenerateHeightmap( (int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				( x, y ) => IslandShape( x, y,
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				TerrainSeed ),
				TerrainMaxHeight,
				SmoothingPasses );
			float[,] splatmap = GenerateSplatmap( heightmap, _splatthresholds );
			SKBitmap image = HeightmapToImage( heightmap );
			SKBitmap splat = SplatmapToImage( splatmap, _splatcolors );
			SaveImage( image, (OutputPath + "\\TerrainGenerationUtility_preview.png") );
			SaveSplatmapAsPng( splat, (OutputPath + "\\TerrainGenerationUtility_splat_export.png") );
			EditorUtility.OpenFile( (OutputPath + "\\TerrainGenerationUtility_preview.png") );
			EditorUtility.OpenFile( (OutputPath + "\\TerrainGenerationUtility_splat_export.png") );
		};
		ExportButton.Clicked += () =>
		{
			Log.Info( "Export!" );
			float[,] heightmap = GenerateHeightmap( (int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				( x, y ) => IslandShape( x, y,
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				TerrainSeed ),
				TerrainMaxHeight,
				SmoothingPasses );
			SaveRaw( heightmap, (OutputPath + "\\TerrainGenerationUtility_export.raw") );
		};
		ApplyTerrainButton.Clicked += () =>
		{
			Log.Info( "Applied!" );

		};
		ApplyTerrainButton.Enabled = false;

		Show();
	}

	// Generates a simple procedural heightmap
	public static float[,] GenerateHeightmap( int width, int height, Func<int, int, float> generator, float maxHeight, int smoothpasses )
	{
		float[,] heightmap = new float[width, height];
		float actualMaxHeight = float.MinValue;

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				heightmap[x, y] = generator( x, y );
				if ( heightmap[x, y] > actualMaxHeight )
				{
					actualMaxHeight = heightmap[x, y];
				}
			}
		}

		// Scale all values down by the actual max height and up to the specified max height
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				heightmap[x, y] = (heightmap[x, y] / actualMaxHeight) * maxHeight;
			}
		}

		return SmoothHeightmap( heightmap, smoothpasses );
	}

	// Smooths the heightmap using a simple box blur with adjustable strength
	private static float[,] SmoothHeightmap( float[,] heightmap, int smoothingPasses )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );
		float[,] smoothed = new float[width, height];

		for ( int pass = 0; pass < smoothingPasses; pass++ )
		{
			for ( int y = 0; y < height; y++ )
			{
				for ( int x = 0; x < width; x++ )
				{
					float sum = 0;
					int count = 0;

					// Iterate through neighbors
					for ( int dy = -1; dy <= 1; dy++ )
					{
						for ( int dx = -1; dx <= 1; dx++ )
						{
							int nx = x + dx;
							int ny = y + dy;

							if ( nx >= 0 && nx < width && ny >= 0 && ny < height )
							{
								sum += heightmap[nx, ny];
								count++;
							}
						}
					}

					smoothed[x, y] = sum / count;
				}
			}

			// Copy smoothed values back to the original heightmap for the next pass
			for ( int y = 0; y < height; y++ )
			{
				for ( int x = 0; x < width; x++ )
				{
					heightmap[x, y] = smoothed[x, y];
				}
			}
		}
		return smoothed;
	}

	// Island shape generator with directional distortion and noise
	public static float IslandShape( int x, int y, int width, int height, int seed )
	{
		float nx = (x / (float)width) * 2 - 1; // Normalize x to range [-1, 1]
		float ny = (y / (float)height) * 2 - 1; // Normalize y to range [-1, 1]

		float distance = (float)Math.Sqrt( nx * nx + ny * ny ); // Distance from center
		float edgeFalloff = 1.0f - Math.Clamp( distance, 0, 1 ); // Falloff towards the edges

		// Random directional offset to reduce symmetry
		float randomOffsetX = 0.5f;
		float randomOffsetY = -0.3f;

		// Distortion to make the shape less circular
		float distortion = (float)(
			OpenSimplex2S.Noise2( seed, (nx + randomOffsetX) * 4, (ny + randomOffsetY) * 4 ) * 0.2 +
			OpenSimplex2S.Noise2( seed, (nx + randomOffsetX) * 8, (ny + randomOffsetY) * 8 ) * 0.1);

		edgeFalloff += distortion;

		// Multi-layer noise for more organic shapes
		float noise = OpenSimplex2S.Noise2( seed, x * 0.03 + randomOffsetX, y * 0.03 + randomOffsetY ) * 0.5f;
		noise += OpenSimplex2S.Noise2( seed, x * 0.1, y * 0.1 ) * 0.3f;
		noise += OpenSimplex2S.Noise2( seed, x * 0.2, y * 0.2 ) * 0.2f;

		// Combine edge falloff and noise
		return Math.Clamp( edgeFalloff * (0.6f + noise), 0, 1 );
	}


	// Converts a heightmap to a grayscale image using SkiaSharp
	public static SKBitmap HeightmapToImage( float[,] heightmap )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );
		SKBitmap bitmap = new SKBitmap( width, height );

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				int intensity = (int)(heightmap[x, y] * 255);
				intensity = Math.Clamp( intensity, 0, 255 );
				bitmap.SetPixel( x, y, new SKColor( (byte)intensity, (byte)intensity, (byte)intensity ) );
			}
		}
		return bitmap;
	}

	public byte[] ConvertSKBitmapToBytes( SKBitmap bitmap, SKEncodedImageFormat format, int quality = 100 )
	{
		// Create an SKImage from the SKBitmap
		using ( var image = SKImage.FromBitmap( bitmap ) )
		{
			// Encode the image to the desired format (e.g., PNG, JPEG)
			using ( var data = image.Encode( format, quality ) )
			{
				// Convert SKData to a byte array
				return data.ToArray();
			}
		}
	}

	// Saves an SKBitmap as an image file
	public static void SaveImage( SKBitmap bitmap, string filename )
	{
		using var image = SKImage.FromBitmap( bitmap );
		using var data = image.Encode( SKEncodedImageFormat.Png, 100 );
		using var stream = System.IO.File.OpenWrite( filename );
		data.SaveTo( stream );
	}

	public static void SaveRaw( float[,] heightmap, string filename )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		using var fileStream = new FileStream( filename, FileMode.Create, FileAccess.Write );
		using var binaryWriter = new BinaryWriter( fileStream );

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				// Scales image data to 16-Bit
				ushort value = (ushort)(Math.Clamp( heightmap[x, y], 0, 1 ) * 65535);
				binaryWriter.Write( value );
			}
		}
		Console.WriteLine( $"Heightmap 16-bit RAW file saved to {filename}" );
	}

	public static float[,] GenerateSplatmap( float[,] heightmap, float[] thresholds )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );
		float[,] splatmap = new float[width, height];

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				float heightValue = heightmap[x, y];

				// Determine splat layer based on thresholds
				for ( int i = 0; i < thresholds.Length; i++ )
				{
					if ( heightValue <= thresholds[i] )
					{
						splatmap[x, y] = i;
						break;
					}
				}
			}
		}

		return splatmap;
	}

	public static SKBitmap SplatmapToImage( float[,] splatmap, SKColor[] colors )
	{
		int width = splatmap.GetLength( 0 );
		int height = splatmap.GetLength( 1 );
		SKBitmap bitmap = new SKBitmap( width, height );

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				// Map the splatmap value to a valid layer index
				int layer = (int)Math.Clamp( splatmap[x, y], 0, colors.Length - 1 );

				// Assign the corresponding color
				SKColor color = colors[layer];
				bitmap.SetPixel( x, y, color );
			}
		}

		return bitmap;
	}

	public static void SaveSplatmapAsPng( SKBitmap bitmap, string filename )
	{
		// Create an SKImage from the bitmap
		using var pixmap = bitmap.PeekPixels();
		using var image = SKImage.FromPixels( pixmap );

		// Encode the SKImage to PNG format
		using var data = image.Encode( SKEncodedImageFormat.Png, 100 );

		// Write the encoded data to a file
		using var stream = File.OpenWrite( filename );
		data.SaveTo( stream );

		Console.WriteLine( $"Splatmap PNG file saved to {filename}" );
	}

	public static void SaveSplatmapRaw( float[,] splatmap, string filename )
	{
		int width = splatmap.GetLength( 0 );
		int height = splatmap.GetLength( 1 );
		using var fileStream = new FileStream( filename, FileMode.Create, FileAccess.Write );
		using var binaryWriter = new BinaryWriter( fileStream );

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				binaryWriter.Write( splatmap[x, y] );
			}
		}

		Console.WriteLine( $"Splatmap RAW file saved to {filename}" );
	}
}
