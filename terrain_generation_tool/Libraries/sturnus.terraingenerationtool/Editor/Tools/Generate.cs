using Editor;
using Editor.ShaderGraph;
using Editor.Widgets;
using Sandbox;
using Sandbox.Tools;
using SkiaSharp;
using Sturnus.TerrainGenerationTool.Hills;
using Sturnus.TerrainGenerationTool.Islands;
using Sturnus.TerrainGenerationTool.Mountainous;
using Sturnus.TerrainGenerationTool.Volcanic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
namespace Sturnus.TerrainGenerationTool.Generation;

	public class TerrainGenerationToolGenerate : Widget
	{
		string[,] TerrainShapeArray = new string[4, 2]
		{
			{ "Island", "circle" },
			{ "Mountainous", "terrain" },
			{ "Volcanic", "volcano" },
			{ "Normal", "square" }
		};
		enum TerrainDimensionsEnum : int
		{
			x512 = 512,
			x1024 = 1024,
			x2048 = 2048,
			x4096 = 4096,
			X8192 = 8192
		}

		enum TerrainShapeEnum : int
		{
			Island = 1,
			Mountainous = 2,
			Volcanic = 3,
			Normal = 4
		}
		TerrainDimensionsEnum TerrainDimensions { get; set; } = TerrainDimensionsEnum.x512;
		TerrainShapeEnum TerrainShape { get; set; } = TerrainShapeEnum.Island;
		[Range( 0.1f, 1f, 0.01f, true, true )] float TerrainMaxHeight { get; set; } = 0.5f;
		long TerrainSeed { get; set; } = 1234567890;
		[Range( 1, 20, 1, true, true )] int SmoothingPasses { get; set; } = 10;
		bool DomainWarping { get; set; } = true;
		[Range( 0.1f, 1f, 0.01f, true, true )] float DomainWarpingSize { get; set; } = 0.25f;
		[Range( 0.1f, 1f, 0.01f, true, true )] float DomainWarpingStrength { get; set; } = 0.15f;
		bool ErosionSimulation { get; set; } = false;
		Gradient SplatMapGradient = new Gradient( new Gradient.ColorFrame( 0.0f, Color.Cyan ), new Gradient.ColorFrame( 0.2f, Color.Red ), new Gradient.ColorFrame( 0.4f, Color.Yellow ), new Gradient.ColorFrame( 0.5f, Color.Green ), new Gradient.ColorFrame( 0.65f, Color.White ), new Gradient.ColorFrame( 0.8f, Color.Magenta ) );
		SKColor[] _splatcolors { get; set; }
		float[] _splatthresholds = { 0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.05f };
		float[,] _heightmap;

		public TerrainGenerationToolGenerate( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();
			SplatMapGradient.Blending = Gradient.BlendMode.Stepped;

			var property = new ControlSheet();
			property.SetMinimumColumnWidth( 0, 250 );
			property.AddProperty( this, x => x.TerrainShape );
			property.AddProperty( this, x => x.TerrainDimensions );
			property.AddProperty( this, x => x.TerrainMaxHeight );
			property.AddProperty( this, x => x.TerrainSeed );
			property.AddProperty( this, x => x.SmoothingPasses );
			property.AddProperty( this, x => x.SplatMapGradient );
			property.AddProperty( this, x => x.DomainWarping );
			property.AddProperty( this, x => x.DomainWarpingSize );
			property.AddProperty( this, x => x.DomainWarpingStrength );

			Layout.Add( property );

			var GenerateButton = new Button.Primary("Generate","auto_awesome");
			Layout.Add( GenerateButton );

			GenerateButton.Clicked += () =>
			{
				List<float> _splatthresholdtime = new List<float>();
				List<SKColor> _splatmapgradients = new List<SKColor>();
				foreach ( var color in SplatMapGradient.Colors )
				{
					_splatthresholdtime.Add( color.Time );
					_splatmapgradients.Add( new SKColor( color.Value.ToColor32().r, color.Value.ToColor32().g, color.Value.ToColor32().b, color.Value.ToColor32().a ) );
				}
				_splatthresholds = _splatthresholdtime.ToArray();
				_splatcolors = _splatmapgradients.ToArray();
				if ( TerrainShape == TerrainShapeEnum.Island )
				{
					_heightmap = GenerateHeightmap( (int)TerrainDimensions ,
					(int)TerrainDimensions,
					( x, y ) => IslandShapes.DefaultIsland( x, y,
					(int)TerrainDimensions,
					(int)TerrainDimensions,
					TerrainSeed,
					DomainWarping,
					DomainWarpingSize,
					DomainWarpingStrength ),
					TerrainMaxHeight,
					SmoothingPasses );

				}
				if ( TerrainShape == TerrainShapeEnum.Mountainous )
				{
					_heightmap = GenerateHeightmap( (int)TerrainDimensions,
					(int)TerrainDimensions,
					( x, y ) => MountainousShapes.DefaultMountain( x, y,
					(int)TerrainDimensions,
					(int)TerrainDimensions,
					TerrainSeed,
					DomainWarping,
					DomainWarpingSize,
					DomainWarpingStrength ),
					TerrainMaxHeight,
					SmoothingPasses );
				}
				if ( TerrainShape == TerrainShapeEnum.Volcanic )
				{
					_heightmap = GenerateHeightmap( (int)TerrainDimensions,
					(int)TerrainDimensions,
					( x, y ) => VolcanicShapes.DefaultVolcanic( x, y,
					(int)TerrainDimensions,
					(int)TerrainDimensions,
					TerrainSeed,
					DomainWarping,
					DomainWarpingSize,
					DomainWarpingStrength ),
					TerrainMaxHeight,
					SmoothingPasses );
				}
				if ( TerrainShape == TerrainShapeEnum.Normal )
				{
					_heightmap = GenerateHeightmap( (int)TerrainDimensions,
					(int)TerrainDimensions,
					( x, y ) => HillsShapes.DefaultHills( x, y,
					(int)TerrainDimensions,
					(int)TerrainDimensions,
					TerrainSeed,
					DomainWarping,
					DomainWarpingSize,
					DomainWarpingStrength ),
					TerrainMaxHeight,
					SmoothingPasses );
				}
				if ( ErosionSimulation )
				{
					_heightmap = ApplyHydraulicErosion( _heightmap, iterations: 10, rainRate: 0.05f, evaporationRate: 0.005f, sedimentCapacity: 0.5f );
				}

				/*GeneratePreviewFile( GenerationPath );

				string preview_image_path = Path.Combine( GenerationLocalPath, $"TerrainGenerationUtility_preview.png" );
				_preview_image_texture = Texture.Load( Editor.FileSystem.Mounted, preview_image_path );
				PreviewImage.Texture = _preview_image_texture;

				string preview_splatmap_path = Path.Combine( GenerationLocalPath, $"TerrainGenerationUtility_splat_preview.png" );
				_preview_splatmap_texture = Texture.Load( Editor.FileSystem.Mounted, preview_splatmap_path );
				PreviewSplatmap.Texture = _preview_splatmap_texture;*/
				var heightmapushort = ConvertFloatArrayToUShortArray( _heightmap );
			};
		}

		float[,] HeightMap()
		{
			return _heightmap;
		}


		private void GeneratePreviewFile( string path )
		{
			//Create TerrainGenerationTool folder if it doesn't exist.
			Directory.CreateDirectory( path );
			string previewfile = Path.Combine( path, $"TerrainGenerationUtility_preview.png" );
			string splatfile = Path.Combine( path, $"TerrainGenerationUtility_splat_preview.png" );

			SKBitmap image = HeightmapToImage( _heightmap );
			SaveImage( image, previewfile );
			float[,] splatmap = GenerateSplatmap( _heightmap, _splatthresholds, TerrainMaxHeight );
			SKBitmap splat = SplatmapToImage( splatmap, _splatcolors );
			SaveSplatmapAsPng( splat, splatfile );
		}

		private void GenerateImageFiles( string output_path )
		{
			string UsingDomainWarping = "";
			string UsingErosionEmulation = "";

			if ( DomainWarping )
			{
				UsingDomainWarping = "_warp";
			}

			if ( ErosionSimulation )
			{
				UsingErosionEmulation = "_erosion";
			}

			//Create TerrainGenerationTool folder if it doesn't exist.
			Directory.CreateDirectory( output_path );
			string rawfile = Path.Combine( output_path, $"TerrainGenerationUtility_export_{TerrainShape}{UsingDomainWarping}{UsingErosionEmulation}.raw" );
			string previewfile = Path.Combine( output_path, $"TerrainGenerationUtility_preview_{TerrainShape}{UsingDomainWarping}{UsingErosionEmulation}.png" );
			string splatfile = Path.Combine( output_path, $"TerrainGenerationUtility_splat_export_{TerrainShape}{UsingDomainWarping}{UsingErosionEmulation}.png" );

			//Export RAW HeightMap file
			SaveRaw( _heightmap, rawfile );
			//Generate & Export Preview image for widget
			SKBitmap image = HeightmapToImage( _heightmap );
			SaveImage( image, previewfile );
			//Generate & Export SplatMap image
			float[,] splatmap = GenerateSplatmap( _heightmap, _splatthresholds, TerrainMaxHeight );
			SKBitmap splat = SplatmapToImage( splatmap, _splatcolors );
			SaveSplatmapAsPng( splat, splatfile );
		}

		// Generates a simple procedural heightmap
		public float[,] GenerateHeightmap( int width, int height, Func<int, int, float> generator, float maxHeight, int smoothpasses )
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

		public static float[,] ApplyHydraulicErosion( float[,] heightmap, int iterations, float rainRate, float evaporationRate, float sedimentCapacity )
		{
			int width = heightmap.GetLength( 0 );
			int height = heightmap.GetLength( 1 );

			// Create water and sediment layers
			float[,] water = new float[width, height];
			float[,] sediment = new float[width, height];

			// Perform erosion for a number of iterations
			for ( int iter = 0; iter < iterations; iter++ )
			{
				// Step 1: Add water (simulate rain)
				for ( int y = 0; y < height; y++ )
				{
					for ( int x = 0; x < width; x++ )
					{
						water[x, y] += rainRate;
					}
				}

				// Step 2: Calculate water flow and sediment transport
				float[,] newWater = new float[width, height];
				float[,] deltaSediment = new float[width, height];

				for ( int y = 1; y < height - 1; y++ )
				{
					for ( int x = 1; x < width - 1; x++ )
					{
						float totalFlow = 0.0f;
						float[] flow = new float[4]; // Left, Right, Up, Down

						// Calculate flow to each neighbor
						for ( int i = 0; i < 4; i++ )
						{
							int nx = x + (i == 0 ? -1 : i == 1 ? 1 : 0);
							int ny = y + (i == 2 ? -1 : i == 3 ? 1 : 0);

							float heightDifference = (heightmap[x, y] + water[x, y]) - (heightmap[nx, ny] + water[nx, ny]);
							if ( heightDifference > 0 )
							{
								flow[i] = heightDifference;
								totalFlow += heightDifference;
							}
						}

						// Normalize flow and update water
						for ( int i = 0; i < 4; i++ )
						{
							int nx = x + (i == 0 ? -1 : i == 1 ? 1 : 0);
							int ny = y + (i == 2 ? -1 : i == 3 ? 1 : 0);

							if ( totalFlow > 0 )
							{
								float normalizedFlow = flow[i] / totalFlow;
								float transfer = normalizedFlow * water[x, y];

								newWater[nx, ny] += transfer;
								newWater[x, y] -= transfer;

								// Sediment transport
								float sedimentTransfer = normalizedFlow * sediment[x, y];
								deltaSediment[nx, ny] += sedimentTransfer;
								deltaSediment[x, y] -= sedimentTransfer;
							}
						}
					}
				}

				// Step 3: Erode and deposit sediment
				for ( int y = 1; y < height - 1; y++ )
				{
					for ( int x = 1; x < width - 1; x++ )
					{
						float maxSediment = water[x, y] * sedimentCapacity;
						if ( sediment[x, y] > maxSediment )
						{
							// Deposit excess sediment
							heightmap[x, y] += sediment[x, y] - maxSediment;
							sediment[x, y] = maxSediment;
						}
						else
						{
							// Erode terrain to match sediment capacity
							float erosion = maxSediment - sediment[x, y];
							heightmap[x, y] -= erosion;
							sediment[x, y] += erosion;
						}
					}
				}

				// Step 4: Evaporate water
				for ( int y = 0; y < height; y++ )
				{
					for ( int x = 0; x < width; x++ )
					{
						water[x, y] = Math.Max( 0, water[x, y] - evaporationRate );
					}
				}
			}

			return heightmap;
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

		public static ushort[] ConvertFloatArrayToUShortArray( float[,] input, float scale = 65535.0f )
		{
			// Get the dimensions of the 2D array
			int rows = input.GetLength( 0 );
			int cols = input.GetLength( 1 );

			// Initialize the 1D ushort array
			ushort[] output = new ushort[rows * cols];

			// Iterate over the 2D array row by row
			int index = 0;
			for ( int row = 0; row < rows; row++ )
			{
				for ( int col = 0; col < cols; col++ )
				{
					// Convert the float to ushort, scaling if necessary
					float value = input[row, col];
					value = Math.Clamp( value, 0.0f, 1.0f ); // Ensure the float is in the 0 to 1 range
					output[index++] = (ushort)(value * scale);
				}
			}

			return output;
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

			// Encode the SKImage to PNG format
			using var data = image.Encode( SKEncodedImageFormat.Png, 100 );

			// Write the encoded data to a file
			using var stream = File.OpenWrite( filename );
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
		public static float[,] GenerateSplatmap( float[,] heightmap, float[] thresholds, float maxHeight )
		{
			int width = heightmap.GetLength( 0 );
			int height = heightmap.GetLength( 1 );
			float[,] splatmap = new float[width, height];

			for ( int y = 0; y < height; y++ )
			{
				for ( int x = 0; x < width; x++ )
				{
					// Normalize height value relative to the maximum height
					float normalizedHeight = heightmap[x, y] / maxHeight;

					// Determine splat layer based on thresholds
					for ( int i = 0; i < thresholds.Length; i++ )
					{
						if ( normalizedHeight <= thresholds[i] )
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

		[TerrainGenerationTool]
		[Title( "Generate" )]
		[Icon( "auto_awesome" )]
		[Order( 1 )]
		internal static Widget TerrainGenerationTool()
		{
			var canvas = new TerrainGenerationToolGenerate( null );

			return canvas;
		}
	}
