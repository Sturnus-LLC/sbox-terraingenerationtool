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

using Sturnus.TerrainGenerationTool;
using Sandbox.Utility;
using System.Threading;
using Sturnus.TerrainGenerationTool.RiverStream;
using Sandbox.Services;
using System.Reflection;
using static TerrainGenerationTool;

[Dock( "Editor", "Terrain Generation Tool", "terrain" )]
public class TerrainGenerationTool : Widget
{
	public string GenerationPath { get; set; } = Editor.FileSystem.Content.GetFullPath( "" ) + "\\TerrainGenerationTool\\";
	public string GenerationLocalPath { get; set; } = "\\TerrainGenerationTool\\";
	public string ExportPath { get; set; } = Project.Current.RootDirectory + "\\Assets\\";

	HashSet<string> TerrainCategoryArray { get; set; } = new HashSet<string>();
	HashSet<string> TerrainShapeArray { get; set; } = new HashSet<string>();

	List<Type> terrainCategoryClassesTypes = new List<Type> { typeof( Islands ), typeof( Mountainous ), typeof( Planetary ), typeof( Realistic ), typeof( Sea ), typeof( Volcanic ) };
	List<Type> terrainShapeMethodTypes { get; set; }


	enum TerrainDimensions : int
	{
		x512 = 512,
		x1024 = 1024,
		x2048 = 2048,
		x4096 = 4096,
		X8192 = 8192
	}

	//enum TerrainCategoryEnum;
	DynamicEnum TerrainCategoryEnum = new DynamicEnum();
	DynamicEnum TerrainShapeEnum = new DynamicEnum();

	TerrainDimensions TerrainDimensionsEnum { get; set; } = TerrainDimensions.x512;
	//TerrainCategoryEnum TerrainShapeEnumSelect { get; set; }
	[Range( 0.1f, 1f, 0.01f, true, true )] float TerrainMinHeight { get; set; } = 0.2f;
	[Range( 0.1f, 1f, 0.01f, true, true )] float TerrainMaxHeight { get; set; } = 0.5f;
	[Range( 0.1f, 1f, 0.01f, true, true )] float TerrainPlaneScale { get; set; } = 0.5f;
	long TerrainSeed { get; set; } = 1234567890;
	[Range( 0, 20, 1, true, true )] int SmoothingPasses { get; set; } = 10;
	[Group( "Domain Warping" )] bool DomainWarping { get; set; } = true;
	[Group( "Domain Warping" )][Range( 0.1f, 1f, 0.01f, true, true )] float DomainWarpingSize { get; set; } = 0.25f;
	[Group( "Domain Warping" )][Range( 0.1f, 1f, 0.01f, true, true )] float DomainWarpingStrength { get; set; } = 0.15f;
	bool ErosionSimulation { get; set; } = false;
	[Range( 1f, 25f, 1f, true, true )] int NoiseLayerStacks { get; set; } = 1;

	///
	/// River Carving Variables
	///
	[Group( "River & Stream Carving" )] bool RiverCarvingBool { get; set; } = true;
	[Group( "River & Stream Carving" )][Range( 0.5f, 10f, 0.1f, true, true )] float RiverCarvingFrequency { get; set; } = 1.5f;
	[Group( "River & Stream Carving" )][Range( 0.01f, 5f, 0.01f, true, true )] float RiverCarvingStrength { get; set; } = 0.3f;
	[Group( "River & Stream Carving" )][Range( 0.01f, 0.25f, 0.001f, true, true )] float RiverCarvingDepth { get; set; } = 0.01f;
	[Group( "River & Stream Carving" )][Range( 0.001f, 2f, 0.01f, true, true )] float RiverCarvingWidth { get; set; } = 0.25f;
	[Group( "River & Stream Carving" )][Range( 0.05f, 1f, 0.01f, true, true )] float RiverCarvingSpacing { get; set; } = 0.05f;
	[Group( "River & Stream Carving" )][Range( 0.01f, 1f, 0.01f, true, true )] float RiverCarvingTurbulenceStrength { get; set; } = 0.01f;
	[Group( "River & Stream Carving" )][Range( 0.01f, 10f, 0.01f, true, true )] float RiverCarvingTurbulenceFrequency { get; set; } = 0.01f;
	

	///
	/// Tool Placement Square
	///
	[Group( "Tool Placement" )] bool StagingArea { get; set; } = true;
	[Group( "Tool Placement" )][Range( 1, 100, 1, true, true )] int StagingAreaSize { get; set; } = 10; // Size of the square (in grid units)
	[Group( "Tool Placement" )][Range( 0, 1, 0.01f, true, true )] float StagingAreaHeight { get; set; } = 0.1f; // Height of the flat square
	[Group( "Tool Placement" )][Range( 0, 1, 0.01f, true, true )] float StagingAreaX { get; set; } = 0.1f; // X-center of the square as a ratio
	[Group( "Tool Placement" )][Range( 0, 1, 0.01f, true, true )] float StagingAreaY { get; set; } = 0.1f; // Y-center of the square as a ratio

	Gradient SplatMapGradient = new Gradient( new Gradient.ColorFrame( 0.0f, Color.Cyan ), new Gradient.ColorFrame( 0.25f, Color.Red ), new Gradient.ColorFrame( 0.5f, Color.Yellow ), new Gradient.ColorFrame( 0.75f, Color.Green ) );
	SKColor[] _splatcolors { get; set; }

	float[] _splatthresholds = { 0f, 0.25f, 0.50f, 0.75f };
	float[,] _heightmap;
	float[,] _splatmap;

	Texture _preview_image_texture;
	Editor.TextureEditor.Preview PreviewImage;
	Texture _preview_splatmap_texture;
	Editor.TextureEditor.Preview PreviewSplatmap;

	SegmentedControl ShapeArray;
	SegmentedControl CategoryArray;

	public class DynamicEnum
	{
		private readonly Dictionary<string, int> _values = new Dictionary<string, int>();
		private int _nextValue = 0;

		public void Add( string name )
		{
			if ( !_values.ContainsKey( name ) )
			{
				_values[name] = _nextValue++;
			}
		}

		public int GetValue( string name )
		{
			return _values.TryGetValue( name, out var value ) ? value : -1; // Return -1 if not found
		}

		public string GetName( int key )
		{
			return _values.FirstOrDefault( pair => pair.Value == key ).Key ?? "Unknown"; // Return "Unknown" if not found
		}

		public string[] GetNames()
		{
			return _values.Keys.ToArray();
		}
	}

	public static string[] GetMethodsFromClass( string className )
	{
		// Attempt to get the Type from the class name (fully qualified)
		Type classType = Type.GetType( className );

		if ( classType == null )
		{
			throw new ArgumentException( $"Class '{className}' could not be found. Ensure the namespace is included." );
		}

		List<string> methodNames = new List<string>();

		// Get all public methods (static and instance) from the class
		MethodInfo[] methods = classType.GetMethods( BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static );

		foreach ( var method in methods )
		{
			// Exclude methods not declared in this class
			if ( method.DeclaringType == classType )
			{
				methodNames.Add( method.Name );
			}
		}

		return methodNames.ToArray();
	}

	public string[] GetTerrainCategoryClasses( params Type[] CategoryClasses )
	{
		HashSet<string> methodNames = new HashSet<string>();

		foreach ( Type CategoryClass in CategoryClasses )
		{
			// Get all public static methods from the class
			MethodInfo[] methods = CategoryClass.GetMethods( BindingFlags.Public | BindingFlags.Static );

			foreach ( MethodInfo method in methods )
			{
				// Exclude inherited methods or non-relevant ones
				if ( method.DeclaringType == CategoryClass )
				{
					methodNames.Add( $"{CategoryClass.Name}.{method.Name}" );
					TerrainCategoryEnum.Add( CategoryClass.Name);
					TerrainCategoryArray.Add( CategoryClass.Name );
				}
			}
		}

		return methodNames.ToArray();
	}

	public string[] GetTerrainShapeMethods( Type shapeClass )
	{
		HashSet<string> methodNames = new HashSet<string>();
		MethodInfo[] methods = shapeClass.GetMethods( BindingFlags.Public | BindingFlags.Static );

			foreach ( MethodInfo method in methods )
			{
				// Exclude inherited methods or non-relevant ones
				if ( method.DeclaringType == shapeClass )
				{
					//TerrainShapeArray.Add( shapeClass.Name );
					//TerrainShapeEnum.Add( shapeClass.Name );
				}
			}

		return methodNames.ToArray();
	}

	public static object CallMethod( string className, string methodName, object[] parameters = null )
	{
		// Get the class type
		Type classType = Type.GetType( className );
		if ( classType == null )
		{
			throw new ArgumentException( $"Class '{className}' could not be found." );
		}

		// Get the method info
		MethodInfo method = classType.GetMethod( methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance );
		if ( method == null )
		{
			throw new ArgumentException( $"Method '{methodName}' could not be found in class '{className}'." );
		}

		// Check if the method is static or instance
		object instance = null;
		if ( !method.IsStatic )
		{
			instance = Activator.CreateInstance( classType );
		}

		// Invoke the method
		object result = method.Invoke( instance, parameters );

		// Ensure the return type is compatible
		if ( result is not float )
		{
			throw new InvalidOperationException( $"Method '{methodName}' does not return a float." );
		}

		return result;
	}

	public void InitialShapes()
	{
		ShapeArray.DestroyChildren();
		TerrainShapeArray.Clear();

		string className = $"Sturnus.TerrainGenerationTool.Islands"; // Fully qualified name
		string[] methods = GetMethodsFromClass( className );

		// Print the methods
		foreach ( string method in methods )
		{
			TerrainShapeArray.Add( method );
			TerrainShapeEnum.Add( method );
		}

		foreach ( var shape in TerrainShapeArray )
		{
			ShapeArray.AddOption( shape );
		}
	}

	public TerrainGenerationTool( Widget parent ) : base( parent, false )
	{
		string[] terrainCategoryClasses = GetTerrainCategoryClasses( terrainCategoryClassesTypes.ToArray() );
		string[] terrainShapeMethods = GetTerrainShapeMethods( typeof(Islands) );
		
		//Create TerrainGenerationTool folder if it doesn't exist.
		Directory.CreateDirectory( GenerationPath );

		SplatMapGradient.Blending = Gradient.BlendMode.Stepped;
		MinimumSize = 500;
		var scroll = new ScrollArea( null );
		scroll.Canvas = new Widget( scroll );
		scroll.Canvas.Layout = Layout.Column();
		scroll.Canvas.Layout.Margin = 10;
		Layout = Layout.Column();
		Layout.Add( scroll );
		
		Layout.Margin = 0;
		Layout.Spacing = 5;

		var body = scroll.Canvas.Layout;

		var DimensionsLabel = body.Add( new Label( "Terrain Dimensions" ) );
		var DimensionsEnum = body.Add( new EnumControlWidget( this.GetSerialized().GetProperty( nameof( TerrainDimensionsEnum ) ) ) );


		var CategoryLabel = body.Add( new Label( "Terrain Category" ) );
		CategoryArray = body.Add( new SegmentedControl( ) );
		for ( int i = 0; i < TerrainCategoryArray.ToArray().GetLength( 0 ); i++ )
		{
			// Initialize an empty list to store the values of the current row
			List<string> rowValues = new List<string>();

			rowValues.Add( TerrainCategoryArray.ToArray()[i] );

			// Join the row's values with a comma and print
			CategoryArray.AddOption( rowValues[0] );
			
		}
		var ShapeLabel = body.Add( new Label( "Terrain Shape" ) );
		ShapeArray = body.Add( new SegmentedControl() );
		InitialShapes();
		CategoryArray.MouseClick += () =>
		{
			RebuildShapes();
		};
		body.AddSpacingCell( 5 );
		var MinHeightLabel = body.Add( new Label( "Min Height (relative)" ) );
		var MinHeightFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( TerrainMinHeight ) ) ) );
		body.AddSpacingCell( 5 );
		var MaxHeightLabel = body.Add( new Label( "Max Height (relative)" ) );
		var MaxHeightFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( TerrainMaxHeight ) ) ) );
		body.AddSpacingCell( 5 );
		var TerrainPlaneScaleLabel = body.Add( new Label( "Terrain Plane Scale" ) );
		var TerrainPlaneScaleNumber = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( TerrainPlaneScale ) ) ) );
		body.AddSpacingCell( 5 );
		var TerrainSeedLabel = body.Add( new Label( "Terrain Seed" ) );
		var TerrainSeedNumber = body.Add( new IntegerControlWidget( this.GetSerialized().GetProperty( nameof( TerrainSeed ) ) ) );
		body.AddSpacingCell( 5 );
		var SmoothingPassesLabel = body.Add( new Label( "Smoothing Passes" ) );
		var SmoothingPassesNumber = body.Add( new IntegerControlWidget( this.GetSerialized().GetProperty( nameof( SmoothingPasses ) ) ) );
		body.AddSpacingCell( 5 );
		var NoiseLayerStacksLabel = body.Add( new Label( "Noise Layer Stacks" ) );
		var NoiseLayerStacksNumber = body.Add( new IntegerControlWidget( this.GetSerialized().GetProperty( nameof( NoiseLayerStacks ) ) ) );
		body.AddSpacingCell( 5 );
		var SplatMapColorsLabel = body.Add( new Label( "Splatmap Colors/Threshold" ) );
		var SplatMapColorsNumber = body.Add( new GradientControlWidget( this.GetSerialized().GetProperty( nameof( SplatMapGradient ) ) ) );
		body.AddSpacingCell( 5 );
		var DomainWarpingLabel = body.Add( new Label( "Domain Warping" ) );
		var DomainWarpingBool = body.Add( new BoolControlWidget( this.GetSerialized().GetProperty( nameof( DomainWarping ) ) ) );
		body.AddSpacingCell( 5 );
		if ( DomainWarping )
		{
			var DomainWarpingSizeLabel = body.Add( new Label( "Domain Warping (Size)" ) );
			var DomainWarpingSizeFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( DomainWarpingSize ) ) ) );
			var DomainWarpingStrengthLabel = body.Add( new Label( "Domain Warping (Strength)" ) );
			var DomainWarpingStrengthFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( DomainWarpingStrength ) ) ) );
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
		/*body.AddSpacingCell( 5 );
		var ErosionSimulationLabel = body.Add( new Label( "Erosion Simulation" ) );
		var ErosionSimulationBool = body.Add( new BoolControlWidget( this.GetSerialized().GetProperty( nameof( ErosionSimulation ) ) ) );
		if ( ErosionSimulation )
		{
		}*/

		body.AddSpacingCell( 5 );
		var RiverCarvingLabel = body.Add( new Label( "River Carving" ) );
		var RiverCarvingBoolControl = body.Add( new BoolControlWidget( this.GetSerialized().GetProperty( nameof( RiverCarvingBool ) ) ) );

		if ( RiverCarvingBool )
		{
			var RiverCarvingFrequency = body.Add( new Label( "RiverCarvingFrequency" ));
			var RiverCarvingFrequencyFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( RiverCarvingFrequency ) ) ) );
			/*var RiverCarvingStrength = body.Add( new Label("RiverCarvingStrength"));
			var RiverCarvingStrengthFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty(nameof(RiverCarvingStrength))));*/
			var RiverCarvingDepth = body.Add( new Label("RiverCarvingDepth"));
			var RiverCarvingDepthFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty(nameof(RiverCarvingDepth))));
			var RiverCarvingWidth = body.Add( new Label("RiverCarvingWidth"));
			var RiverCarvingWidthFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty(nameof(RiverCarvingWidth))));
			var RiverCarvingSpacing = body.Add( new Label("RiverCarvingSpacing"));
			var RiverCarvingSpacingFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty(nameof(RiverCarvingSpacing))));
			var RiverCarvingTurbulenceStrength = body.Add( new Label("RiverCarvingTurbulenceStrength"));
			var RiverCarvingTurbulenceStrengthFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty(nameof(RiverCarvingTurbulenceStrength))));
			var RiverCarvingTurbulenceFrequency = body.Add( new Label("RiverCarvingTurbulenceFrequency"));
			var RiverCarvingTurbulenceFrequencyFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty(nameof(RiverCarvingTurbulenceFrequency))));

			RiverCarvingBoolControl.MouseClick += () =>
			{
				if ( RiverCarvingBool )
				{
					RiverCarvingFrequency.Enabled = true;
					RiverCarvingFrequencyFloat.Enabled = true;
					/*RiverCarvingStrength.Enabled = true;
					RiverCarvingStrengthFloat.Enabled = true;*/
					RiverCarvingDepth.Enabled = true;
					RiverCarvingDepthFloat.Enabled = true;
					RiverCarvingWidth.Enabled = true;
					RiverCarvingWidthFloat.Enabled = true;
					RiverCarvingSpacing.Enabled = true;
					RiverCarvingSpacingFloat.Enabled = true;
					RiverCarvingTurbulenceStrength.Enabled = true;
					RiverCarvingTurbulenceStrengthFloat.Enabled = true;
					RiverCarvingTurbulenceFrequency.Enabled = true;
					RiverCarvingTurbulenceFrequencyFloat.Enabled = true;
					RiverCarvingFrequency.Visible = true;
					RiverCarvingFrequencyFloat.Visible = true;
					/*RiverCarvingStrength.Visible = true;
					RiverCarvingStrengthFloat.Visible = true;*/
					RiverCarvingDepth.Visible = true;
					RiverCarvingDepthFloat.Visible = true;
					RiverCarvingWidth.Visible = true;
					RiverCarvingWidthFloat.Visible = true;
					RiverCarvingSpacing.Visible = true;
					RiverCarvingSpacingFloat.Visible = true;
					RiverCarvingTurbulenceStrength.Visible = true;
					RiverCarvingTurbulenceStrengthFloat.Visible = true;
					RiverCarvingTurbulenceFrequency.Visible = true;
					RiverCarvingTurbulenceFrequencyFloat.Visible = true;
				}
				else
				{
					RiverCarvingFrequency.Enabled = false;
					RiverCarvingFrequencyFloat.Enabled = false;
					/*RiverCarvingStrength.Enabled = false;
					RiverCarvingStrengthFloat.Enabled = false;*/
					RiverCarvingDepth.Enabled = false;
					RiverCarvingDepthFloat.Enabled = false;
					RiverCarvingWidth.Enabled = false;
					RiverCarvingWidthFloat.Enabled = false;
					RiverCarvingSpacing.Enabled = false;
					RiverCarvingSpacingFloat.Enabled = false;
					RiverCarvingTurbulenceStrength.Enabled = false;
					RiverCarvingTurbulenceStrengthFloat.Enabled = false;
					RiverCarvingTurbulenceFrequency.Enabled = false;
					RiverCarvingTurbulenceFrequencyFloat.Enabled = false;

					RiverCarvingFrequency.Visible = false;
					RiverCarvingFrequencyFloat.Visible = false;
					/*RiverCarvingStrength.Visible = false;
					RiverCarvingStrengthFloat.Visible = false;*/
					RiverCarvingDepth.Visible = false;
					RiverCarvingDepthFloat.Visible = false;
					RiverCarvingWidth.Visible = false;
					RiverCarvingWidthFloat.Visible = false;
					RiverCarvingSpacing.Visible = false;
					RiverCarvingSpacingFloat.Visible = false;
					RiverCarvingTurbulenceStrength.Visible = false;
					RiverCarvingTurbulenceStrengthFloat.Visible = false;
					RiverCarvingTurbulenceFrequency.Visible = false;
					RiverCarvingTurbulenceFrequencyFloat.Visible = false;
				}
			};
		}
		else
		{
			//RiverStreamCarving = !RiverStreamCarving;
		}
		body.AddSpacingCell( 5 );
		var ToolPlacementLabel = body.Add( new Label( "Staging Area" ) );
		var ToolPlacementBool = body.Add( new BoolControlWidget( this.GetSerialized().GetProperty( nameof( StagingArea ) ) ) );
		if ( StagingArea )
		{
			var StagingAreaSizeLabel = body.Add( new Label( "Staging Area (Size)" ) );
			var StagingAreaSizeFloat = body.Add( new IntegerControlWidget( this.GetSerialized().GetProperty( nameof( StagingAreaSize ) ) ) );
			var StagingAreaHeightLabel = body.Add( new Label( "Staging Area (Height)" ) );
			var StagingAreaHeightFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( StagingAreaHeight ) ) ) );
			var StagingAreaXLabel = body.Add( new Label( "Staging Area (X)" ) );
			var StagingAreaXFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( StagingAreaX ) ) ) );
			var StagingAreaYLabel = body.Add( new Label( "Staging Area (Y)" ) );
			var StagingAreaYFloat = body.Add( new FloatControlWidget( this.GetSerialized().GetProperty( nameof( StagingAreaY ) ) ) );
			ToolPlacementBool.MouseClick += () =>
			{
				if ( StagingArea )
				{
					StagingAreaSizeLabel.Enabled = true;
					StagingAreaSizeFloat.Enabled = true;
					StagingAreaHeightLabel.Enabled = true;
					StagingAreaHeightFloat.Enabled = true;
					StagingAreaXLabel.Enabled = true;
					StagingAreaXFloat.Enabled = true;
					StagingAreaYLabel.Enabled = true;
					StagingAreaYFloat.Enabled = true;
					StagingAreaSizeLabel.Visible = true;
					StagingAreaSizeFloat.Visible = true;
					StagingAreaHeightLabel.Visible = true;
					StagingAreaHeightFloat.Visible = true;
					StagingAreaXLabel.Visible = true;
					StagingAreaXFloat.Visible = true;
					StagingAreaYLabel.Visible = true;
					StagingAreaYFloat.Visible = true;
				}
				else
				{
					StagingAreaSizeLabel.Enabled = false;
					StagingAreaSizeFloat.Enabled = false;
					StagingAreaHeightLabel.Enabled = false;
					StagingAreaHeightFloat.Enabled = false;
					StagingAreaXLabel.Enabled = false;
					StagingAreaXFloat.Enabled = false;
					StagingAreaYLabel.Enabled = false;
					StagingAreaYFloat.Enabled = false;
					StagingAreaSizeLabel.Visible = false;
					StagingAreaSizeFloat.Visible = false;
					StagingAreaHeightLabel.Visible = false;
					StagingAreaHeightFloat.Visible = false;
					StagingAreaXLabel.Visible = false;
					StagingAreaXFloat.Visible = false;
					StagingAreaYLabel.Visible = false;
					StagingAreaYFloat.Visible = false;
				}
			};
		}

		var GenerateButton = Layout.Add( new Button.Primary( "Generate", "auto_awesome", this ) );

		var LayoutRow = Layout.AddRow( 1 );
		//var PreviewLabel = Layout.Add( new Label( "Preview" ) ); //Will attempt to get this working in a future update.
		var _image_preview = new Editor.TextureEditor.Preview( this );
		_image_preview.Texture = _preview_image_texture;
		_image_preview.Size = new Vector2( 512, 512 );
		PreviewImage = LayoutRow.Add( _image_preview, 50 );

		var _splatmap_preview = new Editor.TextureEditor.Preview( this );
		_splatmap_preview.Texture = _preview_splatmap_texture;
		_splatmap_preview.Size = new Vector2( 512, 512 );
		PreviewSplatmap = LayoutRow.Add( _splatmap_preview, 50 );

		/*RenderCanvas = new SceneRenderingWidget( this );
		RenderCanvas.OnPreFrame += OnPreFrame;
		RenderCanvas.FocusMode = FocusMode.Click;
		RenderCanvas.Scene = Scene.CreateEditorScene();
		RenderCanvas.Scene.SceneWorld.AmbientLightColor = Theme.Blue * 0.4f;
		_terrain = new GameObject( RenderCanvas.Scene,true, "terrain" ).GetOrAddComponent<Terrain>( true );
		//_terrain.Storage.SetResolution( 512 );
		_terrain.TerrainSize = 5007;
		_terrain.TerrainHeight = 1000;
		using ( RenderCanvas.Scene.Push() )
		{
			Camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
			Camera.BackgroundColor = Theme.Grey;
			Camera.ZFar = 100000;
			Camera.Enabled = true;
			Camera.WorldPosition = new Vector3( -2000, 0, 2000 );
			Camera.LocalRotation = new Angles( 30, 0, 0 );
			RenderCanvas.Camera = Camera;
		}

		GizmoInstance = RenderCanvas.GizmoInstance;
		Layout.Add( RenderCanvas,1 ); //Will attempt to get this working in a future update.*/

		var ExportButton = Layout.Add( new Button( "Export", "file_download", this ) );
		ExportButton.Tint = "#41AF20";

		var ApplyButton = Layout.Add( new Button( "Apply To Terrain", "file_upload", this ) );
		ApplyButton.Tint = "#AF2020";

		if ( _heightmap == null )
		{
			ExportButton.Enabled = false;
			ApplyButton.Enabled = false;

		}

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

			var fullclass = Type.GetType($"Sturnus.TerrainGenerationTool.{CategoryArray.Selected}");
			Log.Info( fullclass.ToString() );
			var fullclassmethod = fullclass.GetMethod(ShapeArray.Selected);
			Log.Info( fullclassmethod.ToString() );
			_heightmap = GenerateStackedNoise(
				(int)TerrainDimensionsEnum,
				(int)TerrainDimensionsEnum,
				TerrainSeed,
				NoiseLayerStacks,
				1.0f,
				2.0f,
				1.0f,
				0.5f,
				( x, y ) => (float)CallMethod( $"Sturnus.TerrainGenerationTool.{CategoryArray.Selected}", ShapeArray.Selected, new object[] {
				x, y,
				(int)TerrainDimensionsEnum,
				(int)TerrainDimensionsEnum,
				TerrainSeed,
				TerrainMinHeight,
				DomainWarping,
				DomainWarpingSize,
				DomainWarpingStrength
				} ),
				TerrainMaxHeight,
				SmoothingPasses,
				TerrainPlaneScale
			);
			
			if ( ErosionSimulation )
			{

			}

			if ( RiverCarvingBool )
			{
				_heightmap = AddTurbulenceForRivers(
				_heightmap,
				seed: TerrainSeed,
				riverFrequency: RiverCarvingFrequency, // Frequency of rivers
				riverWidth: RiverCarvingWidth, // Width of rivers
				riverDepth: RiverCarvingDepth, // Depth of rivers
				turbulenceFrequency: RiverCarvingTurbulenceFrequency, // Turbulence frequency
				turbulenceStrength: RiverCarvingTurbulenceStrength, // Turbulence strength
				minRiverSpacing: RiverCarvingSpacing, // Minimum spacing between rivers
				slopeSteepness:10f,
				terrainNoiseFrequency: 2.0f, // Matches terrain noise frequency
				terrainNoiseAmplitude: 0.5f // Matches terrain noise amplitude
			);
			}


			if ( _heightmap == null )
			{
				Log.Error( "Heightmap is not generated. Aborting." );
				return;
			}

			if ( StagingArea )
			{
				_heightmap = AddStagingSquare(
				_heightmap,
				StagingAreaSize,
				StagingAreaHeight,
				StagingAreaX,
				StagingAreaY );
			}

			_splatmap = GenerateSplatmap( _heightmap, _splatthresholds, TerrainMaxHeight );

			GeneratePreviewFile( GenerationPath );

			//_terrain.HeightMap.Update( _heightmap_byte ,0,0, (int)TerrainDimensionsEnum , (int)TerrainDimensionsEnum );

			string preview_image_path = Path.Combine( GenerationLocalPath, $"TerrainGenerationUtility_preview.png" );
			_preview_image_texture = Texture.Load( Editor.FileSystem.Mounted, preview_image_path );
			PreviewImage.Texture = _preview_image_texture;

			string preview_splatmap_path = Path.Combine( GenerationLocalPath, $"TerrainGenerationUtility_splat_preview.png" );
			_preview_splatmap_texture = Texture.Load( Editor.FileSystem.Mounted, preview_splatmap_path );
			PreviewSplatmap.Texture = _preview_splatmap_texture;

			ExportButton.Enabled = true;
			ApplyButton.Enabled = true;

		};

		ExportButton.Clicked += () =>
		{
			GenerateImageFiles( ExportPath );
			var PopUp = new PopupWindow( "Export Complete", $"Files exported to {ExportPath}", "Okay" );
			PopUp.Show();
		};

		ApplyButton.Clicked += () =>
		{
			IDictionary<string, Action> WarnDiaglog = new Dictionary<string, Action>(); ;
			WarnDiaglog.Add( "Apply", UpdateTerrain );
			var PopUpWarn = new PopupWindow( "Warning: Terrain Override", "This will override your current scene's terrain data.","Cancel", WarnDiaglog );
			PopUpWarn.Show();
		};
		
		Layout.AddStretchCell();
		Show();
	}

	public void RebuildShapes()
	{
		ShapeArray.DestroyChildren();
		TerrainShapeArray.Clear();

		string className = $"Sturnus.TerrainGenerationTool.{TerrainCategoryEnum.GetName( TerrainCategoryEnum.GetValue( CategoryArray.Selected ) )}"; // Fully qualified name
		string[] methods = GetMethodsFromClass( className );

		// Print the methods
		foreach ( string method in methods )
		{
			TerrainShapeArray.Add( method );
			//Log.Info( method );
		}

		foreach ( var shape in TerrainShapeArray )
		{
			ShapeArray.AddOption( shape );
		}

		ShapeArray.SelectedIndex = 0;
		ShapeArray.Selected = ShapeArray.Children.FirstOrDefault().Name;
		foreach(var test in ShapeArray.Children )
		{
			//Log.Info( test.Name );
		}
		
	}

	private void UpdateTerrain()
	{
		var ActiveScene = Editor.SceneEditorSession.Active.Scene;
		var FirstTerrain = ActiveScene.GetAllComponents<Terrain>().FirstOrDefault();
		FirstTerrain.UpdateMaterialsBuffer();
		FirstTerrain.SyncGPUTexture();
		FirstTerrain.UpdateMaterialsBuffer();
		FirstTerrain.HeightMap.Update( ConvertRawFloatArrayToByteArray( _heightmap ), 0, 0, (int)TerrainDimensionsEnum, (int)TerrainDimensionsEnum );

		FirstTerrain.Storage.HeightMap = ConvertFloatArrayToUShortArray( _heightmap );
		FirstTerrain.UpdateMaterialsBuffer();
		FirstTerrain.SyncGPUTexture();
		FirstTerrain.UpdateMaterialsBuffer();
	}

	private float[,] AddStagingSquare( float[,] heightmap, int squareSize, float squareHeight, float centerX, float centerY )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		// Calculate the center and bounds of the square
		int centerXPixel = (int)(centerX * width);
		int centerYPixel = (int)(centerY * height);
		int halfSize = squareSize / 2;

		int startX = Math.Max( centerXPixel - halfSize, 0 );
		int startY = Math.Max( centerYPixel - halfSize, 0 );
		int endX = Math.Min( centerXPixel + halfSize, width - 1 );
		int endY = Math.Min( centerYPixel + halfSize, height - 1 );

		// Set the height values inside the square to be completely flat
		for ( int y = startY; y <= endY; y++ )
		{
			for ( int x = startX; x <= endX; x++ )
			{
				heightmap[x, y] = squareHeight;
			}
		}

		// Add the slope around the square
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				// Skip the flat square area
				if ( x >= startX && x <= endX && y >= startY && y <= endY )
					continue;

				// Calculate the distance to the nearest edge of the square
				int dx = Math.Max( Math.Abs( x - centerXPixel ) - halfSize, 0 );
				int dy = Math.Max( Math.Abs( y - centerYPixel ) - halfSize, 0 );
				float distanceToSquare = MathF.Sqrt( dx * dx + dy * dy );

				// Calculate the target height for the slope
				float slopeHeight = squareHeight - (distanceToSquare * 0.0038f); // 0.0038f is perfect for players

				// Ensure the slope transitions smoothly into the existing terrain
				heightmap[x, y] = Math.Max( heightmap[x, y], slopeHeight );
			}
		}
		return heightmap;
	}

	private void GeneratePreviewFile( string path )
	{
		//Create TerrainGenerationTool folder if it doesn't exist.
		Directory.CreateDirectory( path );
		string previewfile = Path.Combine( path, $"TerrainGenerationUtility_preview.png" );
		string splatfile = Path.Combine( path, $"TerrainGenerationUtility_splat_preview.png" );

		SKBitmap image = HeightmapToBitMap( _heightmap );
		SaveImage( image, previewfile );
		//float[,] splatmap = GenerateSplatmap( _heightmap, _splatthresholds, TerrainMaxHeight );
		SKBitmap splat = SplatmapToBitMap( _splatmap, _splatcolors );
		SaveSplatmapAsPng( splat, splatfile );
	}

	private void GenerateImageFiles( string output_path )
	{
		string UsingDomainWarping = "";
		string UsingErosionEmulation = "";
		string UsingWaterCarving = "";

		if ( DomainWarping )
		{
			UsingDomainWarping = "_warp";
		}

		if ( ErosionSimulation )
		{
			UsingErosionEmulation = "_erosion";
		}

		if ( RiverCarvingBool )
		{
			UsingWaterCarving = "_watercarving";
		}

		//Create TerrainGenerationTool folder if it doesn't exist.
		Directory.CreateDirectory( output_path );
		string rawfile = Path.Combine( output_path, $"TerrainGenerationUtility_export_{/*TerrainShapeEnumSelect*/null}{UsingDomainWarping}{UsingErosionEmulation}{UsingWaterCarving}.raw" );
		string previewfile = Path.Combine( output_path, $"TerrainGenerationUtility_preview_{/*TerrainShapeEnumSelect*/null}{UsingDomainWarping}{UsingErosionEmulation}{UsingWaterCarving}.png" );
		string splatfile = Path.Combine( output_path, $"TerrainGenerationUtility_splat_export_{/*TerrainShapeEnumSelect*/null}{UsingDomainWarping}{UsingErosionEmulation}{UsingWaterCarving}.png" );

		//Export RAW HeightMap file
		SaveRaw( _heightmap, rawfile );
		Log.Info( $"Raw file generated! - {rawfile}" );
		//Generate & Export Preview image for widget
		SKBitmap image = HeightmapToBitMap( _heightmap );
		SaveImage( image, previewfile );
		Log.Info( $"HeightMap preview file generated! - {previewfile}" );
		//Generate & Export SplatMap image
		float[,] splatmap = GenerateSplatmap( _heightmap, _splatthresholds, TerrainMaxHeight );
		SKBitmap splat = SplatmapToBitMap( splatmap, _splatcolors );
		SaveSplatmapAsPng( splat, splatfile );
		Log.Info( $"Splatmap file generated! - {splatfile}" );

		Log.Info( $"All export files saved! {output_path}" );
	}

	public float[,] GenerateHeightmap( int width, int height, Func<int, int, float> generator, float maxHeight, int smoothpasses )
	{
		float[,] heightmap = new float[width, height];
		float actualMaxHeight = float.MinValue;

		// Use parallel processing to generate heightmap
		object maxLock = new object(); // Lock object for thread safety
		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				float value = generator( x, y );
				heightmap[x, y] = value;

				// Update actual max height (thread-safe)
				lock ( maxLock )
				{
					if ( value > actualMaxHeight )
					{
						actualMaxHeight = value;
					}
				}
			}
		} );

		// Scale all values by the actual max height and up to the specified max height
		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				heightmap[x, y] = (heightmap[x, y] / actualMaxHeight) * maxHeight;
			}
		} );

		// Apply smoothing if needed
		if ( smoothpasses > 0 )
		{
			return SmoothHeightmap( heightmap, smoothpasses );
		}
		else
		{
			return heightmap;
		}
	}

	public static float[,] GenerateStackedNoise(
		int width,
		int height,
		long seed,
		int layers,
		float initialFrequency,
		float frequencyMultiplier,
		float initialAmplitude,
		float amplitudeMultiplier,
		Func<int, int, float> shapeFunction, // Shape function applied after stacking noise
		float maxHeight,
		int smoothingPasses,
		float terrainPlaneScale // New variable to scale the noise

	)
	{
		// Initialize the heightmap with zeros
		float[,] heightmap = new float[width, height];

		// Random offset generator for noise layers
		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float[] xOffsets = new float[layers];
		float[] yOffsets = new float[layers];

		for ( int i = 0; i < layers; i++ )
		{
			xOffsets[i] = random.Next( -100000, 100000 ) / 1000.0f;
			yOffsets[i] = random.Next( -100000, 100000 ) / 1000.0f;
		}

		// Adjust frequency based on TerrainPlaneScale
		float scaleFactor = Math.Clamp( terrainPlaneScale, 0.01f, 1.0f );

		// Multithreaded noise generation
		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				float value = 0.0f;

				for ( int layer = 0; layer < layers; layer++ )
				{
					float frequency = initialFrequency * MathF.Pow( frequencyMultiplier, layer ) / scaleFactor;
					float amplitude = initialAmplitude * MathF.Pow( amplitudeMultiplier, layer );

					// Normalized coordinates adjusted by scale factor
					float nx = (x / (float)width) * frequency;
					float ny = (y / (float)height) * frequency;

					// Apply random offsets
					nx += xOffsets[layer];
					ny += yOffsets[layer];

					// Generate noise
					float noiseValue = OpenSimplex2S.Noise2( (seed + layer) & 0xFFFFFFFF, nx, ny );
					value += Math.Clamp( noiseValue, -1.0f, 1.0f ) * amplitude;
				}

				// Save the computed value to the heightmap
				lock ( heightmap )
				{
					heightmap[x, y] += value;
				}
			}
		} );

		// Normalize the heightmap to the range [0, 1]
		heightmap = NormalizeHeightmap( heightmap );

		// Apply the shape function and amplify its contribution if needed
		float shapeAmplification = 1.2f; // Adjust for stronger shape effects
		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				heightmap[x, y] *= MathF.Pow( shapeFunction( x, y ), shapeAmplification );
			}
		} );

		// Rescale the heightmap to the desired maxHeight
		float currentMax = FindMaxHeight( heightmap );
		if ( currentMax > 0 )
		{
			Parallel.For( 0, height, y =>
			{
				for ( int x = 0; x < width; x++ )
				{
					heightmap[x, y] = (heightmap[x, y] / currentMax) * maxHeight;
				}
			} );
		}

		// Apply smoothing
		if ( smoothingPasses > 0 )
		{
			heightmap = SmoothHeightmap( heightmap, smoothingPasses );
		}

		return heightmap;
	}


	// Helper method to find the maximum height in a heightmap
	private static float FindMaxHeight( float[,] heightmap )
	{
		float max = float.MinValue;
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				if ( heightmap[x, y] > max )
				{
					max = heightmap[x, y];
				}
			}
		}

		return max;
	}


	private static float[,] NormalizeHeightmap( float[,] heightmap )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		float min = float.MaxValue;
		float max = float.MinValue;

		// Find the min and max values
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				float value = heightmap[x, y];
				if ( value < min ) min = value;
				if ( value > max ) max = value;
			}
		}

		// Normalize the values
		float[,] normalized = new float[width, height];
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				normalized[x, y] = (heightmap[x, y] - min) / (max - min);
			}
		}

		return normalized;
	}

	public static float[,] AddTurbulenceForRivers(
		float[,] heightmap,
		long seed,
		float riverFrequency, // Frequency for river placement
		float riverWidth, // Width of the rivers
		float riverDepth, // Depth of the rivers
		float turbulenceFrequency, // Turbulence frequency
		float turbulenceStrength, // Turbulence strength
		float minRiverSpacing, // Minimum spacing between rivers
		float slopeSteepness, // Controls the gradual slope of the riverbanks
		float terrainNoiseFrequency, // Matches terrain surface noise frequency
		float terrainNoiseAmplitude // Matches terrain surface noise amplitude
	)
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );
		float[,] newHeightmap = (float[,])heightmap.Clone();

		Random random = new Random( (int)(seed & 0xFFFFFFFF) );
		float[,] riverPlacementNoise = new float[width, height];

		// Generate river placement noise
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				float nx = x / (float)width;
				float ny = y / (float)height;

				// Noise for river placement
				riverPlacementNoise[x, y] = OpenSimplex2S.Noise2( seed, nx * riverFrequency, ny * riverFrequency );
			}
		}

		// Process heightmap with river carving
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				float nx = x / (float)width;
				float ny = y / (float)height;

				float riverNoise = MathF.Abs( riverPlacementNoise[x, y] ); // Use absolute noise for placement

				// Determine if the point is within the river carving zone
				if ( riverNoise < riverWidth )
				{
					// Calculate the smooth curve effect based on distance from the center
					float distanceFactor = 1.0f - (riverNoise / riverWidth); // 1 at center, 0 at edge
					float smoothDepthReduction = MathF.Pow( distanceFactor, slopeSteepness ) * riverDepth;

					// Add turbulence for a more organic flow
					float turbulence = OpenSimplex2S.Noise2( seed + 1, nx * turbulenceFrequency, ny * turbulenceFrequency )
									   * turbulenceStrength;

					// Apply smooth depth reduction and turbulence
					float reducedHeight = newHeightmap[x, y] - smoothDepthReduction + turbulence;

					// Clamp height to ensure it doesn't rise above the original
					newHeightmap[x, y] = MathF.Max( 0, MathF.Min( newHeightmap[x, y], reducedHeight ) );
				}

				// Enforce minimum spacing between rivers
				if ( riverNoise < minRiverSpacing )
				{
					// Slightly raise the terrain to enforce separation
					newHeightmap[x, y] += (minRiverSpacing - riverNoise) * 0.05f;
				}
			}
		}

		// Add base noise to the entire heightmap after carving
		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				float nx = x / (float)width;
				float ny = y / (float)height;

				// Generate base noise
				float baseNoise = OpenSimplex2S.Noise2( seed + 2, nx * 6f, ny * 6f )
								  * 0.02f;

				// Add noise to the heightmap
				newHeightmap[x, y] = MathF.Max( 0, newHeightmap[x, y] + baseNoise );
			}
		}

		return newHeightmap;
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

	public static byte[] ConvertRawFloatArrayToByteArray( float[,] rawData, float scale = 65535.0f )
	{
		if ( rawData == null )
		{
			throw new ArgumentNullException( nameof( rawData ), "Input rawData cannot be null." );
		}

		int rows = rawData.GetLength( 0 );
		int cols = rawData.GetLength( 1 );

		// Create a byte array with 2 bytes per value
		byte[] byteArray = new byte[rows * cols * 2]; // 2 bytes per ushort

		int index = 0;
		for ( int row = 0; row < rows; row++ )
		{
			for ( int col = 0; col < cols; col++ )
			{
				float value = rawData[row, col];
				value = Math.Clamp( value, 0.0f, 1.0f ); // Ensure value is in the range [0, 1]

				// Convert to 16-bit unsigned integer
				ushort ushortValue = (ushort)(value * scale);

				// Store in byte array (little-endian order)
				byteArray[index++] = (byte)(ushortValue & 0xFF);       // Lower byte
				byteArray[index++] = (byte)((ushortValue >> 8) & 0xFF); // Upper byte
			}
		}

		return byteArray;
	}

	// Converts a heightmap to a grayscale image using SkiaSharp
	public static SKBitmap HeightmapToBitMap( float[,] heightmap )
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

	public static void SaveImage(
	SKBitmap bitmap,
	string filename,
	float rotationDegrees = 270f,
	bool reverseHorizontal = false,
	bool reverseVertical = true
)
	{
		int width = bitmap.Width;
		int height = bitmap.Height;

		// Create a new bitmap to hold the transformed image
		using var transformedBitmap = new SKBitmap( width, height );

		// Create a canvas to draw the transformed image
		using var canvas = new SKCanvas( transformedBitmap );

		// Clear the canvas with transparency
		canvas.Clear( SKColors.Transparent );

		// Apply transformations
		canvas.Save();

		// Translate to the center of the canvas for rotation and flipping
		canvas.Translate( width / 2f, height / 2f );

		// Apply flipping first
		float scaleX = reverseHorizontal ? -1f : 1f;
		float scaleY = reverseVertical ? -1f : 1f;
		canvas.Scale( scaleX, scaleY );

		// Apply rotation
		if ( rotationDegrees != 0 )
		{
			canvas.RotateDegrees( rotationDegrees );
		}

		// Translate back to ensure the image is drawn correctly
		canvas.Translate( -width / 2f, -height / 2f );

		// Draw the original bitmap onto the transformed canvas
		canvas.DrawBitmap( bitmap, 0, 0 );

		// Restore the canvas to finalize the transformations
		canvas.Restore();

		// Flush the canvas
		canvas.Flush();

		// Save the transformed bitmap as a PNG file
		using var pixmap = transformedBitmap.PeekPixels();
		using var image = SKImage.FromPixels( pixmap );
		using var data = image.Encode( SKEncodedImageFormat.Png, 100 );

		using var stream = File.OpenWrite( filename );
		data.SaveTo( stream );
	}

	public static void SaveRaw( float[,] heightmap, string filename, int rotationDegrees = 270, bool reverseHorizontal = true, bool reverseVertical = false )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		// Rotate the heightmap if requested
		if ( rotationDegrees != 0 )
		{
			heightmap = RotateHeightmap( heightmap, rotationDegrees );
			if ( rotationDegrees == 90 || rotationDegrees == 270 )
			{
				// Swap width and height for 90° or 270° rotations
				(width, height) = (height, width);
			}
		}

		// Reverse the heightmap if requested
		if ( reverseHorizontal || reverseVertical )
		{
			heightmap = ReverseHeightmap( heightmap, reverseHorizontal, reverseVertical );
		}

		using var fileStream = new FileStream( filename, FileMode.Create, FileAccess.Write );
		using var binaryWriter = new BinaryWriter( fileStream );

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				// Scale image data to 16-bit
				ushort value = (ushort)(Math.Clamp( heightmap[x, y], 0, 1 ) * 65535);
				binaryWriter.Write( value );
			}
		}
	}

	// Helper method to rotate the heightmap by 90°, 180°, or 270°
	private static float[,] RotateHeightmap( float[,] original, int rotationDegrees )
	{
		int originalWidth = original.GetLength( 0 );
		int originalHeight = original.GetLength( 1 );

		float[,] rotated;

		switch ( rotationDegrees )
		{
			case 90:
				rotated = new float[originalHeight, originalWidth];
				for ( int y = 0; y < originalHeight; y++ )
				{
					for ( int x = 0; x < originalWidth; x++ )
					{
						rotated[y, originalWidth - 1 - x] = original[x, y];
					}
				}
				break;

			case 180:
				rotated = new float[originalWidth, originalHeight];
				for ( int y = 0; y < originalHeight; y++ )
				{
					for ( int x = 0; x < originalWidth; x++ )
					{
						rotated[originalWidth - 1 - x, originalHeight - 1 - y] = original[x, y];
					}
				}
				break;

			case 270:
				rotated = new float[originalHeight, originalWidth];
				for ( int y = 0; y < originalHeight; y++ )
				{
					for ( int x = 0; x < originalWidth; x++ )
					{
						rotated[originalHeight - 1 - y, x] = original[x, y];
					}
				}
				break;

			default:
				throw new ArgumentException( "Rotation must be 0, 90, 180, or 270 degrees." );
		}

		return rotated;
	}

	// Helper method to reverse the heightmap horizontally and/or vertically
	private static float[,] ReverseHeightmap( float[,] original, bool reverseHorizontal, bool reverseVertical )
	{
		int width = original.GetLength( 0 );
		int height = original.GetLength( 1 );

		float[,] reversed = new float[width, height];

		for ( int y = 0; y < height; y++ )
		{
			for ( int x = 0; x < width; x++ )
			{
				int targetX = reverseHorizontal ? width - 1 - x : x;
				int targetY = reverseVertical ? height - 1 - y : y;
				reversed[targetX, targetY] = original[x, y];
			}
		}

		return reversed;
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

				// Interpolate between thresholds for smoother transitions
				for ( int i = 0; i < thresholds.Length - 1; i++ )
				{
					if ( normalizedHeight >= thresholds[i] && normalizedHeight <= thresholds[i + 1] )
					{
						// Linear interpolation between the two layers
						float t = (normalizedHeight - thresholds[i]) / (thresholds[i + 1] - thresholds[i]);
						splatmap[x, y] = i + t; // Interpolated layer index
						break;
					}
				}

				// Assign the last layer if above the highest threshold
				if ( normalizedHeight > thresholds[thresholds.Length - 1] )
				{
					splatmap[x, y] = thresholds.Length - 1;
				}
			}
		}

		return splatmap;
	}

	public static SKBitmap SplatmapToBitMap( float[,] splatmap, SKColor[] colors )
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

	public static void SaveSplatmapAsPng(
	SKBitmap bitmap,
	string filename,
	float rotationDegrees = 270f,
	bool reverseHorizontal = false,
	bool reverseVertical = true
)
	{
		int width = bitmap.Width;
		int height = bitmap.Height;
		using var transformedBitmap = new SKBitmap( width, height );
		using var canvas = new SKCanvas( transformedBitmap );

		// Clear the canvas with transparency
		canvas.Clear( SKColors.Transparent );
		// Apply transformations
		canvas.Save();
		// Translate to the center of the canvas for rotation and flipping
		canvas.Translate( width / 2f, height / 2f );
		// Apply flipping first
		float scaleX = reverseHorizontal ? -1f : 1f;
		float scaleY = reverseVertical ? -1f : 1f;
		canvas.Scale( scaleX, scaleY );

		// Apply rotation
		if ( rotationDegrees != 0 )
		{
			canvas.RotateDegrees( rotationDegrees );
		}

		// Translate back to ensure the image is drawn correctly
		canvas.Translate( -width / 2f, -height / 2f );
		// Draw the original bitmap onto the transformed canvas
		canvas.DrawBitmap( bitmap, 0, 0 );
		// Restore the canvas to finalize the transformations
		canvas.Restore();
		// Flush the canvas
		canvas.Flush();

		// Save the transformed bitmap as a PNG file
		using var pixmap = transformedBitmap.PeekPixels();
		using var image = SKImage.FromPixels( pixmap );
		using var data = image.Encode( SKEncodedImageFormat.Png, 100 );

		using var stream = File.OpenWrite( filename );
		data.SaveTo( stream );
	}
}
