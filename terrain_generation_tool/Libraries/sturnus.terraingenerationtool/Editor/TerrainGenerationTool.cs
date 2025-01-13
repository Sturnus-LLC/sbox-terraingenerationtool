using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing;
using System.IO;
using System.Linq;
using Editor;
using Editor.ShaderGraph.Nodes;
using Editor.Widgets;
using Sandbox;
using SkiaSharp;
using static Sandbox.Gradient;

using Sturnus.TerrainGenerationTool.Islands;
using Sturnus.TerrainGenerationTool.Mountainous;
using Sturnus.TerrainGenerationTool.Volcanic;
using Sturnus.TerrainGenerationTool.Hills;

[Dock( "Editor", "Terrain Generation Tool", "terrain" )]
public class TerrainGenerationTool : Widget
{
	public string GenerationPath { get; set; } = Editor.FileSystem.Content.GetFullPath( "" ) + "\\TerrainGenerationTool\\";
	public string GenerationLocalPath { get; set; } = "\\TerrainGenerationTool\\";
	public string ExportPath { get; set; } = Project.Current.RootDirectory + "\\Assets\\";

	string[,] TerrainShapeArray = new string[4, 2]
	{
		{ "Island", "circle" },
		{ "Mountainous", "terrain" },
		{ "Volcanic", "volcano" },
		{ "Normal", "square" }
	};
	enum TerrainDimensions : int
	{
		x512 = 512,
		x1024 = 1024,
		x2048 = 2048,
		x4096 = 4096,
		X8192 = 8192
	}

	enum TerrainShapeEnum : int
	{
		Island		= 1,
		Mountainous = 2,
		Volcanic	= 3,
		Normal		= 4
	}
	TerrainDimensions TerrainDimensionsEnum { get; set; } = TerrainDimensions.x512;
	TerrainShapeEnum TerrainShapeEnumSelect { get; set; } = TerrainShapeEnum.Island;
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
	//byte[] _heightmap_image;

	Texture _preview_image_texture;
	Editor.TextureEditor.Preview PreviewImage;
	Texture _preview_splatmap_texture;
	Editor.TextureEditor.Preview PreviewSplatmap;

	SceneRenderingWidget RenderCanvas;
	CameraComponent Camera;
	Gizmo.Instance GizmoInstance;
	Terrain _terrain;

	ushort[] _heightmap_ushort;

	public TerrainGenerationTool( Widget parent ) : base( parent, false )
	{

		//Create TerrainGenerationTool folder if it doesn't exist.
		Directory.CreateDirectory( GenerationPath );

		SplatMapGradient.Blending = Gradient.BlendMode.Stepped;
		MinimumSize = 500;
		Layout = Layout.Column();
		Layout.Margin = 20;
		Layout.Spacing = 5;
		var ShapeLabel = Layout.Add( new Label( "Terrain Shape" ) );
		var ShapeArray = Layout.Add( new SegmentedControl( ) );
		for ( int i = 0; i < TerrainShapeArray.GetLength( 0 ); i++ )
		{
			// Initialize an empty list to store the values of the current row
			List<string> rowValues = new List<string>();

			for ( int j = 0; j < TerrainShapeArray.GetLength( 1 ); j++ )
			{
				// Add each value to the list
				rowValues.Add( TerrainShapeArray[i, j] );
			}

			// Join the row's values with a comma and print
			ShapeArray.AddOption( rowValues[0],rowValues[1] );
			
		}
		ShapeArray.MouseClick += () =>
		{
			Enum.TryParse<TerrainShapeEnum>( ShapeArray.Selected, out TerrainShapeEnum terrain_out );
			TerrainShapeEnumSelect = terrain_out;
			Log.Info( TerrainShapeEnumSelect );
		};
		Layout.AddSpacingCell( 5 );
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
		var SplatMapColorsLabel = Layout.Add( new Label( "Splatmap Colors/Threshold" ) );
		var SplatMapColorsNumber = Layout.Add( new GradientControlWidget( this.GetSerialized().GetProperty( nameof( SplatMapGradient ) ) ) );
		Layout.AddSpacingCell( 5 );
		var DomainWarpingLabel = Layout.Add( new Label( "Domain Warping" ) );
		var DomainWarpingBool = Layout.Add( new BoolControlWidget( this.GetSerialized().GetProperty( nameof( DomainWarping ) ) ) );
		/*Layout.AddSpacingCell( 5 );
		var ErosionSimulationLabel = Layout.Add( new Label( "Erosion Simulation" ) );
		var ErosionSimulationBool = Layout.Add( new BoolControlWidget( this.GetSerialized().GetProperty( nameof( ErosionSimulation ) ) ) );*/
		Layout.AddSpacingCell( 5 );
		if ( DomainWarping )
		{
			var DomainWarpingSizeLabel = Layout.Add( new Label( "Domain Warping (Size)" ) );
			var DomainWarpingSizeFloat = Layout.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( DomainWarpingSize ) ) ) );
			var DomainWarpingStrengthLabel = Layout.Add( new Label( "Domain Warping (Strength)" ) );
			var DomainWarpingStrengthFloat = Layout.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( DomainWarpingStrength ) ) ) );
			DomainWarpingBool.MouseClick += () =>
			{
				if ( DomainWarping )
				{
					DomainWarpingSizeLabel.Enabled = true;
					DomainWarpingSizeFloat.Enabled = true;
					DomainWarpingSizeLabel.Visible = true;
					DomainWarpingSizeFloat.Visible = true;

					DomainWarpingStrengthLabel.Enabled = true;
					DomainWarpingStrengthFloat.Enabled = true;
					DomainWarpingStrengthLabel.Visible = true;
					DomainWarpingStrengthFloat.Visible = true;
				}
				else
				{
					DomainWarpingSizeLabel.Enabled = false;
					DomainWarpingSizeFloat.Enabled = false;
					DomainWarpingSizeLabel.Visible = false;
					DomainWarpingSizeFloat.Visible = false;

					DomainWarpingStrengthLabel.Enabled = false;
					DomainWarpingStrengthFloat.Enabled = false;
					DomainWarpingStrengthLabel.Visible = false;
					DomainWarpingStrengthFloat.Visible = false;
				}
			};
		}

		if ( ErosionSimulation )
		{
			/*var DomainWarpingSizeLabel = Layout.Add( new Label( "Domain Warping (Size)" ) );
			var DomainWarpingSizeFloat = Layout.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( DomainWarpingSize ) ) ) );
			var DomainWarpingStrengthLabel = Layout.Add( new Label( "Domain Warping (Strength)" ) );
			var DomainWarpingStrengthFloat = Layout.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( DomainWarpingStrength ) ) ) );
			DomainWarpingBool.MouseClick += () =>
			{
				if ( ErosionSimulation )
				{
					DomainWarpingSizeLabel.Enabled = true;
					DomainWarpingSizeFloat.Enabled = true;
					DomainWarpingSizeLabel.Visible = true;
					DomainWarpingSizeFloat.Visible = true;

					DomainWarpingStrengthLabel.Enabled = true;
					DomainWarpingStrengthFloat.Enabled = true;
					DomainWarpingStrengthLabel.Visible = true;
					DomainWarpingStrengthFloat.Visible = true;
				}
				else
				{
					DomainWarpingSizeLabel.Enabled = false;
					DomainWarpingSizeFloat.Enabled = false;
					DomainWarpingSizeLabel.Visible = false;
					DomainWarpingSizeFloat.Visible = false;

					DomainWarpingStrengthLabel.Enabled = false;
					DomainWarpingStrengthFloat.Enabled = false;
					DomainWarpingStrengthLabel.Visible = false;
					DomainWarpingStrengthFloat.Visible = false;
				}
			};*/
		}

		var GenerateButton = Layout.Add( new Button.Primary( "Generate", "auto_awesome", this ) );

		//var PreviewLabel = Layout.Add( new Label( "Preview" ) ); //Will attempt to get this working in a future update.
		/*var _image_preview = new Editor.TextureEditor.Preview( this );
		_image_preview.Texture = _preview_image_texture;
		_image_preview.Size = new Vector2( 512, 512 );
		PreviewImage = Layout.Add( _image_preview, 50 );

		var _splatmap_preview = new Editor.TextureEditor.Preview( this );
		_splatmap_preview.Texture = _preview_splatmap_texture;
		_splatmap_preview.Size = new Vector2( 512, 512 );
		PreviewSplatmap = Layout.Add( _splatmap_preview, 50 );*/

		RenderCanvas = new SceneRenderingWidget( this );
		RenderCanvas.OnPreFrame += OnPreFrame;
		RenderCanvas.FocusMode = FocusMode.Click;
		RenderCanvas.Scene = Scene.CreateEditorScene();
		RenderCanvas.Scene.SceneWorld.AmbientLightColor = Theme.Blue * 0.4f;

		using ( RenderCanvas.Scene.Push() )
		{
			Camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
			Camera.BackgroundColor = Theme.Grey;
			Camera.ZFar = 4096;
			Camera.Enabled = true;
			Camera.WorldPosition = new Vector3( -1000, 0, 1000 );
			Camera.LocalRotation = new Angles( 45, 0, 0 );
			RenderCanvas.Camera = Camera;
			_terrain = new GameObject( true, "terrain" ).GetOrAddComponent<Terrain>( true );
			
		}

		GizmoInstance = RenderCanvas.GizmoInstance;
		Layout.Add( RenderCanvas,1 ); //Will attempt to get this working in a future update.

		var ExportButton = Layout.Add( new Button( "Export", "file_download", this ) );
		ExportButton.Tint = "#41AF20";

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
			if ( TerrainShapeEnumSelect == TerrainShapeEnum.Island )
			{
				_heightmap = GenerateHeightmap( (int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				( x, y ) => IslandShapes.DefaultIsland( x, y,
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				TerrainSeed,
				DomainWarping,
				DomainWarpingSize,
				DomainWarpingStrength ),
				TerrainMaxHeight,
				SmoothingPasses);
				
			}
			if ( TerrainShapeEnumSelect == TerrainShapeEnum.Mountainous )
			{
				_heightmap = GenerateHeightmap( (int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				( x, y ) => MountainousShapes.DefaultMountain( x, y,
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				TerrainSeed,
				DomainWarping,
				DomainWarpingSize,
				DomainWarpingStrength ),
				TerrainMaxHeight,
				SmoothingPasses );
			}
			if ( TerrainShapeEnumSelect == TerrainShapeEnum.Volcanic )
			{
				_heightmap = GenerateHeightmap( (int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				( x, y ) => VolcanicShapes.DefaultVolcanic( x, y,
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				TerrainSeed,
				DomainWarping,
				DomainWarpingSize,
				DomainWarpingStrength ),
				TerrainMaxHeight,
				SmoothingPasses );
			}
			if ( TerrainShapeEnumSelect == TerrainShapeEnum.Normal )
			{
				_heightmap = GenerateHeightmap( (int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				( x, y ) => HillsShapes.DefaultHills( x, y,
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
				(int)Enum.Parse( typeof( TerrainDimensions ), TerrainDimensionsEnum.ToString() ),
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

			_heightmap_ushort = ConvertFloatArrayToUShortArray( _heightmap );
			_terrain.Storage.HeightMap = _heightmap_ushort;

			GeneratePreviewFile( GenerationPath );

			string preview_image_path = Path.Combine( GenerationLocalPath, $"TerrainGenerationUtility_preview.png" );
			_preview_image_texture = Texture.Load( Editor.FileSystem.Mounted, preview_image_path );
			PreviewImage.Texture = _preview_image_texture;

			string preview_splatmap_path = Path.Combine( GenerationLocalPath, $"TerrainGenerationUtility_splat_preview.png" );
			_preview_splatmap_texture = Texture.Load( Editor.FileSystem.Mounted, preview_splatmap_path );
			PreviewSplatmap.Texture = _preview_splatmap_texture;

		};

		ExportButton.Clicked += () =>
		{
			GenerateImageFiles( ExportPath );
		};
		Layout.AddStretchCell();
		Show();
	}

	ISceneTest current;
	private void OnPreFrame()
	{
		// TODO: We shouldn't be accessing SceneCamera but all this shit needs it
		var camera = GizmoInstance.Input.Camera;
		camera.Position = Camera.WorldPosition;
		camera.Rotation = Camera.WorldRotation;

		GizmoInstance.FirstPersonCamera( camera, RenderCanvas );

		Camera.WorldPosition = camera.Position;
		Camera.WorldRotation = camera.Rotation;

		if ( Gizmo.ControlMode == "firstperson" )
		{
			Gizmo.Draw.Color = Gizmo.HasHovered ? Color.White : Color.Black.WithAlpha( 0.3f );
			Gizmo.Draw.LineSphere( new Sphere( Gizmo.Camera.Position + Gizmo.Camera.Rotation.Forward * 50.0f, 0.1f ) );
		}

		Gizmo.Draw.Color = Color.White.WithAlpha( 0.4f );
		Gizmo.Draw.Plane( Vector3.Zero, Vector3.Up );


		for ( var row = 0; row < 32; row++ )
		{
			var x = row * 1f;
			Vector3 last = 0.0f;

			using ( Gizmo.Scope( $"Line{row}", Transform.Zero.WithPosition( new Vector3( 0, 0, 0 ) ) ) )
			{
				Gizmo.Draw.LineThickness = 1;
				Gizmo.Draw.Color = Color.White;

				for ( var i = 0; i < 32; i++ )
				{
					var y = i * 10.0f;
					var p = new Vector3( x, y, 0f );

					if ( i > 0 )
					{
						Gizmo.Draw.Line( last, p );
					}

					last = p;
				}
			}
		}
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
		string rawfile = Path.Combine( output_path, $"TerrainGenerationUtility_export_{TerrainShapeEnumSelect}{UsingDomainWarping}{UsingErosionEmulation}.raw" );
		string previewfile = Path.Combine( output_path, $"TerrainGenerationUtility_preview_{TerrainShapeEnumSelect}{UsingDomainWarping}{UsingErosionEmulation}.png" );
		string splatfile = Path.Combine( output_path, $"TerrainGenerationUtility_splat_export_{TerrainShapeEnumSelect}{UsingDomainWarping}{UsingErosionEmulation}.png" );

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

	// Hilly terrain generator
	

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

	/*public static SKBitmap HeightmapToImage( float[,] heightmap )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );
		SKBitmap bitmap = new SKBitmap( width, height, SKColorType.Rgba8888, SKAlphaType.Premul );

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				float value = heightmap[x, y];
				int luminance = (int)(value * 255);
				luminance = Math.Clamp( luminance, 0, 255 );

				// Set Alpha as fully opaque and Luminance for grayscale
				bitmap.SetPixel( x, y, new SKColor( (byte)luminance, (byte)luminance, (byte)luminance, 255 ) );
			}
		}

		return bitmap;
	}*/

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
}
