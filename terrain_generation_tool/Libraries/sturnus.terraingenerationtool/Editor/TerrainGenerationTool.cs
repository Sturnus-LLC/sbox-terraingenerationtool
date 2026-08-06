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
using System.Threading.Tasks;
using Parallel = System.Threading.Tasks.Parallel;
using Sturnus.TerrainGenerationTool.RiverStream;
using Sandbox.Services;
using System.Reflection;
using static TerrainGenerationTool;

[EditorApp( "Terrain Generation Tool", "terrain", "Generate procedural terrain with a realtime 3D preview" )]
public class TerrainGenerationTool : BaseWindow
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

	public enum SplatDispersionMode
	{
		Evenly,
		Natural
	}

	//enum TerrainCategoryEnum;
	DynamicEnum TerrainCategoryEnum = new DynamicEnum();
	DynamicEnum TerrainShapeEnum = new DynamicEnum();

	TerrainDimensions TerrainDimensionsEnum { get; set; } = TerrainDimensions.x512;
	//TerrainCategoryEnum TerrainShapeEnumSelect { get; set; }
	[Step( 0.01f ),MinMax(0.1f,1f)] float TerrainMinHeight { get; set; } = 0.2f;
	[Step( 0.01f),MinMax(0.1f,1f)] float TerrainMaxHeight { get; set; } = 0.5f;
	[Step( 0.01f),MinMax(0.1f,1f)] float TerrainPlaneScale { get; set; } = 0.5f;
	long TerrainSeed { get; set; } = 1234567890;
	[Step( 1 ), MinMax( 1,20 )] int SmoothingPasses { get; set; } = 10;
	[Group( "Domain Warping" )] bool DomainWarping { get; set; } = true;
	[Group( "Domain Warping" )][Step( 0.01f ),MinMax(0.1f,1f)] float DomainWarpingSize { get; set; } = 0.25f;
	[Group( "Domain Warping" )][Step( 0.01f ),MinMax(0.1f,1f)] float DomainWarpingStrength { get; set; } = 0.15f;
	bool ErosionSimulation { get; set; } = false;
	[Step( 1f),MinMax(1f,25f)] int NoiseLayerStacks { get; set; } = 1;

	///
	/// River Carving Variables
	///
	[Property][Group( "River & Stream Carving" )] bool RiverCarvingBool { get; set; } = true;
	[Property][Group( "River & Stream Carving" )][Step( 0.1f), MinMax( 0.5f, 10f )] float RiverCarvingFrequency { get; set; } = 1.5f;
	[Property][Group( "River & Stream Carving" )][Step( 0.01f),MinMax(0.01f,5f)] float RiverCarvingStrength { get; set; } = 0.3f;
	[Property][Group( "River & Stream Carving" )][Step( 0.001f),MinMax(0.01f,0.25f)] float RiverCarvingDepth { get; set; } = 0.01f;
	[Property][Group( "River & Stream Carving" )][Step( 0.01f),MinMax(0.001f,2f)] float RiverCarvingWidth { get; set; } = 0.25f;
	[Property][Group( "River & Stream Carving" )][Step( 0.01f),MinMax(0.05f,1f)] float RiverCarvingSpacing { get; set; } = 0.05f;
	[Property][Group( "River & Stream Carving" )][Step( 0.01f),MinMax(0.01f,1f)] float RiverCarvingTurbulenceStrength { get; set; } = 0.01f;
	[Property][Group( "River & Stream Carving" )][Step( 0.01f),MinMax(0.01f,10f)] float RiverCarvingTurbulenceFrequency { get; set; } = 0.01f;
	

	///
	/// Tool Placement Square
	///
	[Group( "Tool Placement" )] bool StagingArea { get; set; } = true;
	[Group( "Tool Placement" )][Step( 1),MinMax( 1, 100 )] int StagingAreaSize { get; set; } = 10; // Size of the square (in grid units)
	[Group( "Tool Placement" )][Step(0.01f),MinMax(0,1)] float StagingAreaHeight { get; set; } = 0.1f; // Height of the flat square
	[Group( "Tool Placement" )][Step(0.01f),MinMax(0,1)] float StagingAreaX { get; set; } = 0.1f; // X-center of the square as a ratio
	[Group( "Tool Placement" )][Step(0.01f),MinMax(0,1)] float StagingAreaY { get; set; } = 0.1f; // Y-center of the square as a ratio

	Gradient SplatMapGradient = new Gradient( new Gradient.ColorFrame( 0.0f, Color.Cyan ), new Gradient.ColorFrame( 0.25f, Color.Red ), new Gradient.ColorFrame( 0.5f, Color.Yellow ), new Gradient.ColorFrame( 0.75f, Color.Green ) );
	SKColor[] _splatcolors { get; set; }

	[Step( 1 ), MinMax( 2, 32 )] int SplatLayerCount { get; set; } = 8;
	[Step( 1 ), MinMax( 1, 8 )] int SplatMapCount { get; set; } = 1;
	SplatDispersionMode SplatDispersion { get; set; } = SplatDispersionMode.Evenly;
	[Step( 0.05f ), MinMax( 0f, 1f )] float SplatBlendStrength { get; set; } = 0.35f;

	[Property] bool PreviewSplatMaterials { get; set; } = false;

	float[] _splatthresholds = { 0f, 0.25f, 0.50f, 0.75f };
	float[,] _heightmap;
	float[,] _splatmap;
	float[,] _previewHeightmap;

	TerrainMaterial[] _previewMaterials;
	int _previewMaterialsGeneration = 0;
	List<Editor.Asset> _localTmatAssets;

	Texture _preview_image_texture;
	Editor.TextureWidget PreviewImage;
	Texture _preview_splatmap_texture;
	Editor.TextureWidget PreviewSplatmap;

	SceneRenderingWidget RenderCanvas;
	CameraComponent Camera;
	Gizmo.Instance GizmoInstance;
	GameObject _previewGO;
	Terrain _previewTerrain;
	TerrainStorage _previewStorage;
	GameObject _splatOverlayGO;
	ModelRenderer _splatOverlayRenderer;
	Mesh _overlayMesh;
	float[] _overlayTargetHeights;
	float[] _overlayCurrentHeights;
	Color32[] _overlayCurrentColors;
	float[,] _overlaySplatmap;
	bool _overlayUseSplatColors;
	bool _overlayAnimating;
	List<Color> _splatColorCache = new();
	List<float> _currentFrameTimes;
	List<float> _targetFrameTimes;
	bool _gradientAnimating;
	bool _isAnimatingGradient;
	const float PreviewMorphSpeed = 8f;
	const float MeshMorphSpeed = PreviewMorphSpeed * 0.25f;
	float _orbitDistance = 20000f;
	float _orbitAngle = 0f;
	float _orbitPitch = 30f;
	bool _autoSpin = true;
	FloatSlider ZoomSlider;
	const float SpinSpeed = 8f; // degrees per second
	const int PreviewResolution = 512;
	const float PreviewTerrainSize = 20000f;
	const float PreviewTerrainHeight = 5000f;

	SerializedObject _serialized;
	bool _previewDirty;
	float _lastPreviewRegen = float.MinValue;
	bool _isGenerating = false;
	int _generationToken = 0;

	List<Widget> _domainWarpingWidgets = new();
	List<Widget> _riverCarvingWidgets = new();
	List<Widget> _stagingAreaWidgets = new();

	Dictionary<string, Widget> _propsPages = new();
	SegmentedControl _propsTabBar;
	Widget _propsContent;
	string _activePropsTab;
	Button _randomizeMaterialsButton;
	Label _materialLoadingLabel;
	GradientControlWidget _gradientControlWidget;

	WrapSelector ShapeArray;
	WrapSelector CategoryArray;

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
			if ( string.IsNullOrEmpty( name ) ) return -1;
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

	public TerrainGenerationTool() : base()
	{
		WindowTitle = "Terrain Generation Tool";
		SetWindowIcon( "terrain" );
		MinimumSize = new Vector2( 1000, 700 );
		Size = new Vector2( 1500, 900 );
		StartCentered = true;

		_serialized = this.GetSerialized();
		_serialized.OnPropertyChanged += OnSerializedPropertyChanged;

		string[] terrainCategoryClasses = GetTerrainCategoryClasses( terrainCategoryClassesTypes.ToArray() );
		string[] terrainShapeMethods = GetTerrainShapeMethods( typeof(Islands) );
		
		//Create TerrainGenerationTool folder if it doesn't exist.
		Directory.CreateDirectory( GenerationPath );

		SplatMapGradient.Blending = Gradient.BlendMode.Stepped;

		Layout = Layout.Row();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		// ---------- Left: options panel ----------
		var scroll = new ScrollArea( this );
		scroll.Canvas = new Widget( scroll );
		scroll.Canvas.Layout = Layout.Column();
		scroll.Canvas.Layout.Margin = 10;
		scroll.Canvas.Layout.Spacing = 5;
		scroll.MinimumWidth = 400;
		scroll.MaximumWidth = 480;
		Layout.Add( scroll );

		var body = scroll.Canvas.Layout;

		// ---------- Left: grouped property tabs ----------
		var propsRoot = body.Add( new Widget( null ), 1 );
		propsRoot.Layout = Layout.Column();
		propsRoot.Layout.Spacing = 5;

		_propsTabBar = propsRoot.Layout.Add( new SegmentedControl() );
		_propsTabBar.ShowText = true;
		_propsTabBar.FixedHeight = Theme.RowHeight * 1.6f;
		_propsTabBar.OnSelectedChanged += ( name ) => SelectPropsTab( name );

		_propsContent = propsRoot.Layout.Add( new Widget( null ), 1 );
		_propsContent.Layout = Layout.Column();
		_propsContent.Layout.Margin = 0;
		_propsContent.Layout.Alignment = TextFlag.Top;

		// --- Terrain Type tab ---
		var typePage = CreatePropsPage();

		typePage.Layout.Add( new Label( "Terrain Dimensions" ) );
		typePage.Layout.Add( new EnumControlWidget( _serialized.GetProperty( nameof( TerrainDimensionsEnum ) ) ) );

		typePage.Layout.Add( new Label( "Terrain Category" ) );
		CategoryArray = typePage.Layout.Add( new WrapSelector() );
		for ( int i = 0; i < TerrainCategoryArray.ToArray().GetLength( 0 ); i++ )
		{
			List<string> rowValues = new List<string>();
			rowValues.Add( TerrainCategoryArray.ToArray()[i] );
			CategoryArray.AddOption( rowValues[0] );
		}
		typePage.Layout.Add( new Label( "Terrain Shape" ) );
		ShapeArray = typePage.Layout.Add( new WrapSelector() );
		InitialShapes();
		CategoryArray.OnSelectedChanged += ( _ ) =>
		{
			RebuildShapes();
			_previewDirty = true;
		};
		ShapeArray.OnSelectedChanged += ( _ ) => _previewDirty = true;
		if ( CategoryArray.Children.Count() > 0 )
		{
			CategoryArray.SelectedIndex = 0;
			CategoryArray.Selected = CategoryArray.Children.First().Name;
		}
		RebuildShapes();
		if ( ShapeArray.Children.Count() > 0 )
		{
			ShapeArray.SelectedIndex = 0;
			ShapeArray.Selected = ShapeArray.Children.First().Name;
		}
		AddPropsTab( "Terrain Type", "terrain", typePage, "Terrain dimensions, category and shape" );

		// --- Height / Scale tab ---
		var heightPage = CreatePropsPage();

		heightPage.Layout.Add( new Label( "Min Height (relative)" ) );
		heightPage.Layout.Add( FloatSlider( nameof( TerrainMinHeight ) ) );
		heightPage.Layout.Add( new Label( "Max Height (relative)" ) );
		heightPage.Layout.Add( FloatSlider( nameof( TerrainMaxHeight ) ) );
		heightPage.Layout.Add( new Label( "Terrain Plane Scale" ) );
		heightPage.Layout.Add( FloatSlider( nameof( TerrainPlaneScale ) ) );
		heightPage.Layout.Add( new Label( "Terrain Seed" ) );
		var seedRow = heightPage.Layout.AddRow();
		seedRow.Spacing = 4;
		var seedControl = seedRow.Add( new IntegerControlWidget( _serialized.GetProperty( nameof( TerrainSeed ) ) ), 1 );
		seedRow.Add( new IconButton( "casino", RandomizeSeed, this )
		{
			ToolTip = "Randomize seed",
			IconSize = 16,
			FixedSize = new Vector2( 26, 26 )
		} );
		AddPropsTab( "Height/Scale", "straighten", heightPage, "Terrain height, plane scale and seed" );

		// --- Smooth / Noise tab ---
		var noisePage = CreatePropsPage();

		noisePage.Layout.Add( new Label( "Smoothing Passes" ) );
		noisePage.Layout.Add( IntSlider( nameof( SmoothingPasses ) ) );
		noisePage.Layout.Add( new Label( "Noise Layer Stacks" ) );
		noisePage.Layout.Add( IntSlider( nameof( NoiseLayerStacks ) ) );
		AddPropsTab( "Smooth/Noise", "grain", noisePage, "Terrain smoothing and noise layers" );

		// --- Splat tab ---
		var splatPage = CreatePropsPage();

		splatPage.Layout.Add( new Label( "Splat Layer Count" ) );
		splatPage.Layout.Add( IntSlider( nameof( SplatLayerCount ) ) );
		splatPage.Layout.Add( new Label( "Splat Map Count" ) );
		splatPage.Layout.Add( IntSlider( nameof( SplatMapCount ) ) );
		splatPage.Layout.Add( new Label( "Dispersion" ) );
		splatPage.Layout.Add( new EnumControlWidget( _serialized.GetProperty( nameof( SplatDispersion ) ) ) );
		splatPage.Layout.Add( new Label( "Blend Strength" ) );
		splatPage.Layout.Add( FloatSlider( nameof( SplatBlendStrength ) ) );
		splatPage.Layout.Add( new Label( "Splatmap Colors/Threshold" ) );
		_gradientControlWidget = new GradientControlWidget( _serialized.GetProperty( nameof( SplatMapGradient ) ) );
		splatPage.Layout.Add( _gradientControlWidget );
		splatPage.Layout.Add( new Label( "Preview Materials" ) );
		splatPage.Layout.Add( new BoolControlWidget( _serialized.GetProperty( nameof( PreviewSplatMaterials ) ) ) );
		var materialHint = new Label( "Assigns random local .tmat terrain materials from your project's assets to the splat layers so you can preview the material blending on the terrain." );
		materialHint.SetStyles( "font-size: 10px; color: #888;" );
		materialHint.WordWrap = true;
		materialHint.MaximumWidth = 260;
		splatPage.Layout.Add( materialHint );
		var randomizeRow = splatPage.Layout.AddRow();
		_randomizeMaterialsButton = randomizeRow.Add( new Button( "Randomize Materials", "casino" ) );
		_randomizeMaterialsButton.Clicked += RandomizeMaterials;
		_materialLoadingLabel = randomizeRow.Add( new Label( "Loading..." ) );
		_materialLoadingLabel.SetStyles( "font-size: 10px; color: #888;" );
		_materialLoadingLabel.Visible = false;
		_materialLoadingLabel.WordWrap = true;
		_materialLoadingLabel.MaximumWidth = 120;
		AddPropsTab( "Splat", "palette", splatPage, "Splatmap layers, maps, colors and dispersion" );

		// --- Warping tab ---
		var warpPage = CreatePropsPage();

		warpPage.Layout.Add( new Label( "Domain Warping" ) );
		warpPage.Layout.Add( new BoolControlWidget( _serialized.GetProperty( nameof( DomainWarping ) ) ) );
		var DomainWarpingSizeLabel = warpPage.Layout.Add( new Label( "Domain Warping (Size)" ) );
		var DomainWarpingSizeFloat = warpPage.Layout.Add( FloatSlider( nameof( DomainWarpingSize ) ) );
		var DomainWarpingStrengthLabel = warpPage.Layout.Add( new Label( "Domain Warping (Strength)" ) );
		var DomainWarpingStrengthFloat = warpPage.Layout.Add( FloatSlider( nameof( DomainWarpingStrength ) ) );
		_domainWarpingWidgets.AddRange( new Widget[] { DomainWarpingSizeLabel, DomainWarpingSizeFloat, DomainWarpingStrengthLabel, DomainWarpingStrengthFloat } );
		AddPropsTab( "Warping", "blur_on", warpPage, "Domain warping options" );

		// --- River tab ---
		var riverPage = CreatePropsPage();

		riverPage.Layout.Add( new Label( "River Carving" ) );
		riverPage.Layout.Add( new BoolControlWidget( _serialized.GetProperty( nameof( RiverCarvingBool ) ) ) );
		var RiverCarvingFrequencyLabel = riverPage.Layout.Add( new Label( "RiverCarvingFrequency" ) );
		var RiverCarvingFrequencyFloat = riverPage.Layout.Add( FloatSlider( nameof( RiverCarvingFrequency ) ) );
		/*var RiverCarvingStrength = riverPage.Layout.Add( new Label("RiverCarvingStrength"));
		var RiverCarvingStrengthFloat = riverPage.Layout.Add( FloatSlider( nameof(RiverCarvingStrength) ) );*/
		var RiverCarvingDepthLabel = riverPage.Layout.Add( new Label( "RiverCarvingDepth" ) );
		var RiverCarvingDepthFloat = riverPage.Layout.Add( FloatSlider( nameof( RiverCarvingDepth ) ) );
		var RiverCarvingWidthLabel = riverPage.Layout.Add( new Label( "RiverCarvingWidth" ) );
		var RiverCarvingWidthFloat = riverPage.Layout.Add( FloatSlider( nameof( RiverCarvingWidth ) ) );
		var RiverCarvingSpacingLabel = riverPage.Layout.Add( new Label( "RiverCarvingSpacing" ) );
		var RiverCarvingSpacingFloat = riverPage.Layout.Add( FloatSlider( nameof( RiverCarvingSpacing ) ) );
		var RiverCarvingTurbulenceStrengthLabel = riverPage.Layout.Add( new Label( "RiverCarvingTurbulenceStrength" ) );
		var RiverCarvingTurbulenceStrengthFloat = riverPage.Layout.Add( FloatSlider( nameof( RiverCarvingTurbulenceStrength ) ) );
		var RiverCarvingTurbulenceFrequencyLabel = riverPage.Layout.Add( new Label( "RiverCarvingTurbulenceFrequency" ) );
		var RiverCarvingTurbulenceFrequencyFloat = riverPage.Layout.Add( FloatSlider( nameof( RiverCarvingTurbulenceFrequency ) ) );
		_riverCarvingWidgets.AddRange( new Widget[]
		{
			RiverCarvingFrequencyLabel, RiverCarvingFrequencyFloat,
			/*RiverCarvingStrength, RiverCarvingStrengthFloat,*/
			RiverCarvingDepthLabel, RiverCarvingDepthFloat,
			RiverCarvingWidthLabel, RiverCarvingWidthFloat,
			RiverCarvingSpacingLabel, RiverCarvingSpacingFloat,
			RiverCarvingTurbulenceStrengthLabel, RiverCarvingTurbulenceStrengthFloat,
			RiverCarvingTurbulenceFrequencyLabel, RiverCarvingTurbulenceFrequencyFloat
		} );
		AddPropsTab( "River", "water", riverPage, "River carving options" );

		// --- Staging tab ---
		var stagingPage = CreatePropsPage();

		stagingPage.Layout.Add( new Label( "Staging Area" ) );
		stagingPage.Layout.Add( new BoolControlWidget( _serialized.GetProperty( nameof( StagingArea ) ) ) );
		var StagingAreaSizeLabel = stagingPage.Layout.Add( new Label( "Staging Area (Size)" ) );
		var StagingAreaSizeFloat = stagingPage.Layout.Add( IntSlider( nameof( StagingAreaSize ) ) );
		var StagingAreaHeightLabel = stagingPage.Layout.Add( new Label( "Staging Area (Height)" ) );
		var StagingAreaHeightFloat = stagingPage.Layout.Add( FloatSlider( nameof( StagingAreaHeight ) ) );
		var StagingAreaXLabel = stagingPage.Layout.Add( new Label( "Staging Area (X)" ) );
		var StagingAreaXFloat = stagingPage.Layout.Add( FloatSlider( nameof( StagingAreaX ) ) );
		var StagingAreaYLabel = stagingPage.Layout.Add( new Label( "Staging Area (Y)" ) );
		var StagingAreaYFloat = stagingPage.Layout.Add( FloatSlider( nameof( StagingAreaY ) ) );
		_stagingAreaWidgets.AddRange( new Widget[]
		{
			StagingAreaSizeLabel, StagingAreaSizeFloat,
			StagingAreaHeightLabel, StagingAreaHeightFloat,
			StagingAreaXLabel, StagingAreaXFloat,
			StagingAreaYLabel, StagingAreaYFloat
		} );
		AddPropsTab( "Staging", "square_foot", stagingPage, "Staging area placement" );

		body.AddSpacingCell( 5 );

		var GenerateButton = body.Add( new Button.Primary( "Generate", "auto_awesome", this ) );

		var ExportButton = body.Add( new Button( "Export", "file_download", this ) );
		ExportButton.Tint = "#41AF20";

		var ApplyButton = body.Add( new Button( "Apply To Terrain", "file_upload", this ) );
		ApplyButton.Tint = "#AF2020";

		if ( _heightmap == null )
		{
			ExportButton.Enabled = false;
			ApplyButton.Enabled = false;

		}

		GenerateButton.Clicked += () =>
		{
			BuildSplatColors();

			int fullRes = (int)TerrainDimensionsEnum;
			_heightmap = BuildHeightmap(
				fullRes, fullRes,
				CategoryArray.Selected, ShapeArray.Selected,
				TerrainSeed, NoiseLayerStacks, TerrainMinHeight, TerrainMaxHeight,
				DomainWarping, DomainWarpingSize, DomainWarpingStrength,
				SmoothingPasses, TerrainPlaneScale,
				RiverCarvingBool, RiverCarvingFrequency, RiverCarvingWidth, RiverCarvingDepth,
				RiverCarvingTurbulenceFrequency, RiverCarvingTurbulenceStrength, RiverCarvingSpacing,
				StagingArea, StagingAreaSize, StagingAreaHeight, StagingAreaX, StagingAreaY );
			if ( _heightmap == null )
			{
				Log.Error( "Heightmap is not generated. Aborting." );
				return;
			}

			_splatmap = GenerateSplatmap( _heightmap, _splatthresholds, TerrainMaxHeight, SplatLayerCount, SplatDispersion, SplatBlendStrength );

			GeneratePreviewFile( GenerationPath );

			BaseFileSystem fileSystem = Editor.FileSystem.Mounted;
			string preview_image_path = Path.Combine( GenerationLocalPath, $"TerrainGenerationUtility_preview.png" );
			_preview_image_texture = Texture.LoadFromFileSystem( preview_image_path, fileSystem );
			PreviewImage.Texture = _preview_image_texture;

			string preview_splatmap_path = Path.Combine( GenerationLocalPath, $"TerrainGenerationUtility_splat_preview.png" );
			_preview_splatmap_texture = Texture.LoadFromFileSystem( preview_splatmap_path, fileSystem );
			PreviewSplatmap.Texture = _preview_splatmap_texture;

			ExportButton.Enabled = true;
			ApplyButton.Enabled = true;

			UpdatePreviewTerrain( _heightmap );

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
		
		body.AddStretchCell();

		// ---------- Right: tabbed preview panel ----------
		var rightPanel = Layout.Add( new Widget( null ), 1 );
		rightPanel.Layout = Layout.Column();
		rightPanel.Layout.Spacing = 0;

		var PreviewTabs = rightPanel.Layout.Add( new VerticalTabWidget( this ), 1 );
		PreviewTabs.StateCookie = "TerrainGenerationTool.PreviewTabs";

		RenderCanvas = new SceneRenderingWidget( this );
		RenderCanvas.OnPreFrame += OnPreFrame;
		RenderCanvas.FocusMode = FocusMode.Click;
		RenderCanvas.Scene = Scene.CreateEditorScene();
		RenderCanvas.Scene.SceneWorld.AmbientLightColor = Color.FromBytes( 135, 206, 235 ) * 0.45f;

		// 3D preview tab
		PreviewTabs.AddPage( "3D Preview", "landscape", RenderCanvas, "3D terrain preview" );

		// Height/Color maps tab
		var mapsPage = new Widget( null );
		mapsPage.Layout = Layout.Row();
		mapsPage.Layout.Spacing = 5;

		var _image_preview = new Editor.TextureWidget();
		_image_preview.Texture = _preview_image_texture;
		_image_preview.Size = new Vector2( 512, 512 );
		PreviewImage = mapsPage.Layout.Add( _image_preview, 50 );

		var _splatmap_preview = new Editor.TextureWidget();
		_splatmap_preview.Texture = _preview_splatmap_texture;
		_splatmap_preview.Size = new Vector2( 512, 512 );
		PreviewSplatmap = mapsPage.Layout.Add( _splatmap_preview, 50 );

		PreviewTabs.AddPage( "Height/Color Maps", "grid_view", mapsPage, "Heightmap and splatmap preview" );

		using ( RenderCanvas.Scene.Push() )
		{
			Camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
			Camera.BackgroundColor = Color.FromBytes( 135, 206, 235 );
			Camera.ZFar = 100000;
			Camera.Enabled = true;
			PositionCameraForOrbit();
			RenderCanvas.Camera = Camera;

			var sun = new GameObject( true, "sun" ).GetOrAddComponent<DirectionalLight>( false );
			sun.WorldRotation = Rotation.From( 45, 45, 0 );
			sun.LightColor = Color.White;
			sun.SkyColor = Color.FromBytes( 135, 206, 235 );
			sun.Enabled = true;

			var sun2 = new GameObject( true, "sun2" ).GetOrAddComponent<DirectionalLight>( false );
			sun2.WorldRotation = Rotation.From( -30, 135, 0 );
			sun2.LightColor = Color.White * 0.3f;
			sun2.SkyColor = Color.FromBytes( 135, 206, 235 );
			sun2.Enabled = true;
		}

		GizmoInstance = RenderCanvas.GizmoInstance;

		// Create the preview terrain - a real Terrain component in the preview scene
		using ( RenderCanvas.Scene.Push() )
		{
			_previewGO = new GameObject( true, "terrain preview" );
			_previewTerrain = _previewGO.AddComponent<Terrain>( false );
			_previewStorage = new TerrainStorage();
			_previewStorage.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
			_previewStorage.SetResolution( PreviewResolution );
			_previewStorage.TerrainSize = PreviewTerrainSize;
			_previewStorage.TerrainHeight = PreviewTerrainHeight;
			_previewTerrain.Storage = _previewStorage;
			_previewTerrain.TerrainSize = PreviewTerrainSize;
			_previewTerrain.TerrainHeight = PreviewTerrainHeight;
			// Terrain spans [0, TerrainSize] from its origin - shift it so it's centered on the origin
			_previewGO.WorldPosition = new Vector3( -PreviewTerrainSize * 0.5f, -PreviewTerrainSize * 0.5f, 0f );
			_previewTerrain.Enabled = true;

			// Overlay mesh that shows the splatmap colors when the material preview is off.
			// Slightly offset above the terrain so it doesn't z-fight with the terrain surface.
			// Parented to the terrain GO (which is centered at origin), so the mesh uses local coords.
			_splatOverlayGO = new GameObject( true, "splat overlay" );
			_splatOverlayGO.Parent = _previewGO;
			_splatOverlayGO.LocalPosition = new Vector3( 0, 0, 1f );
			_splatOverlayRenderer = _splatOverlayGO.AddComponent<ModelRenderer>();
			_splatOverlayRenderer.MaterialOverride = Material.Load( "materials/default/vertex_color.vmat" );
			_splatOverlayGO.Enabled = false;
		}

		// Zoom slider at the bottom of the preview panel
		var zoomRow = rightPanel.Layout.AddRow();
		zoomRow.Margin = new Sandbox.UI.Margin( 8, 4, 8, 6 );
		zoomRow.Spacing = 8;

		zoomRow.Add( new IconButton( "zoom_out", () => ZoomSlider.Value = MathF.Max( ZoomSlider.Minimum, ZoomSlider.Value - 500f ), this ) { IconSize = 16, FixedSize = new Vector2( 22, 22 ) } );
		ZoomSlider = zoomRow.Add( new FloatSlider( this ), 1 );
		ZoomSlider.Minimum = 10000f;
		ZoomSlider.Maximum = 40000f;
		ZoomSlider.Step = 500f;
		ZoomSlider.Value = 40000f - _orbitDistance + 10000f;
		ZoomSlider.OnValueEdited = UpdateOrbitFromZoom;
		zoomRow.Add( new IconButton( "zoom_in", () => ZoomSlider.Value = MathF.Min( ZoomSlider.Maximum, ZoomSlider.Value + 500f ), this ) { IconSize = 16, FixedSize = new Vector2( 22, 22 ) } );

		ApplyConditionalVisibility();
		LoadFacepunchMaterialsAsync();
		RegeneratePreview();
		Show();
	}

	void OnSerializedPropertyChanged( SerializedProperty prop )
	{
		// Intermediate frames written during gradient animation shouldn't retrigger a regen
		if ( !_isAnimatingGradient )
			_previewDirty = true;

		if ( prop is null ) return;

		switch ( prop.Name )
		{
			case nameof( DomainWarping ):
				SetWidgetsVisible( _domainWarpingWidgets, DomainWarping );
				break;
			case nameof( RiverCarvingBool ):
				SetWidgetsVisible( _riverCarvingWidgets, RiverCarvingBool );
				break;
			case nameof( StagingArea ):
				SetWidgetsVisible( _stagingAreaWidgets, StagingArea );
				break;
			case nameof( SplatLayerCount ):
			case nameof( SplatDispersion ):
				// Resample the gradient into evenly spaced stops so the colors/thresholds match the layer count
				ResampleSplatGradient();
				if ( PreviewSplatMaterials )
				{
					_previewMaterials = null;
					RandomizeMaterialsAsync();
				}
				break;
			case nameof( PreviewSplatMaterials ):
				if ( PreviewSplatMaterials && ( _previewMaterials == null || _previewMaterials.Length < Math.Max( SplatLayerCount, 2 ) ) )
				{
					RandomizeMaterials();
				}
				if ( !PreviewSplatMaterials )
				{
					_previewMaterials = null;
				}
				break;
		}
	}

	void ResampleSplatGradient()
	{
		int layerCount = Math.Max( SplatLayerCount, 2 );

		// Seed the persistent color cache from the current gradient first so user edits are kept.
		if ( _splatColorCache.Count == 0 && SplatMapGradient.Colors != null && SplatMapGradient.Colors.Count() > 0 )
		{
			foreach ( var frame in SplatMapGradient.Colors )
				_splatColorCache.Add( frame.Value );
		}

		// Add new colors to the end as layers grow - existing colors keep their index.
		while ( _splatColorCache.Count < layerCount )
			_splatColorCache.Add( RandomBrightColor() );

		// Compute the target stop positions.
		float[] thresholds;
		var source = _previewHeightmap ?? _heightmap;
		if ( SplatDispersion == SplatDispersionMode.Natural && source != null )
		{
			thresholds = ComputeNaturalThresholds( source, layerCount );
		}
		else
		{
			thresholds = new float[layerCount];
			for ( int i = 0; i < layerCount; i++ )
			{
				thresholds[i] = (float)i / (layerCount - 1);
			}
		}
		_splatthresholds = thresholds;

		// First build: set the gradient directly.
		if ( _currentFrameTimes is null )
		{
			_currentFrameTimes = thresholds.ToList();
			_targetFrameTimes = thresholds.ToList();
			ApplyGradientFromFrames();
			return;
		}

		int oldCount = _currentFrameTimes.Count;

		// New frames (added layers) enter from the right and slide left to their target.
		if ( layerCount > oldCount )
		{
			for ( int i = oldCount; i < layerCount; i++ )
				_currentFrameTimes.Add( 1f );
			_targetFrameTimes = thresholds.ToList();
		}
		// Removed frames slide out to the right (target 1.0) and get dropped when they arrive.
		else if ( layerCount < oldCount )
		{
			// Existing frames keep their current positions; the extra ones head right.
			var newTargets = thresholds.ToList();
			while ( newTargets.Count < oldCount )
				newTargets.Add( 1f );
			_targetFrameTimes = newTargets;
		}
		// Same count - just retarget the existing frames.
		else
		{
			_targetFrameTimes = thresholds.ToList();
		}

		_gradientAnimating = true;
	}

	void ApplyGradientFromFrames()
	{
		// Only include frames that are still "in play" (haven't slid off the right edge yet).
		var frames = new List<Gradient.ColorFrame>();
		for ( int i = 0; i < _currentFrameTimes.Count && i < _splatColorCache.Count; i++ )
		{
			float t = Math.Clamp( _currentFrameTimes[i], 0f, 1f );
			frames.Add( new Gradient.ColorFrame( t, _splatColorCache[i] ) );
		}

		SplatMapGradient = new Gradient( frames.ToArray() );
		SplatMapGradient.Blending = Gradient.BlendMode.Stepped;

		_isAnimatingGradient = true;
		try
		{
			_serialized.GetProperty( nameof( SplatMapGradient ) )?.SetValue( SplatMapGradient );
		}
		finally
		{
			_isAnimatingGradient = false;
		}
	}

	/// <summary>
	/// Eases the gradient color stops toward their target positions so palette changes
	/// slide in/out on the scale instead of snapping.
	/// </summary>
	void UpdateGradientAnimation()
	{
		if ( !_gradientAnimating || _currentFrameTimes is null || _targetFrameTimes is null ) return;

		float t = 1f - MathF.Exp( -PreviewMorphSpeed * RealTime.Delta );

		float maxDelta = 0f;
		for ( int i = 0; i < _currentFrameTimes.Count && i < _targetFrameTimes.Count; i++ )
		{
			float delta = _targetFrameTimes[i] - _currentFrameTimes[i];
			_currentFrameTimes[i] += delta * t;
			maxDelta = MathF.Max( maxDelta, MathF.Abs( delta ) );
		}

		// Drop frames that have slid off the right edge (removed layers)
		if ( _currentFrameTimes.Count > _targetFrameTimes.Count )
		{
			while ( _currentFrameTimes.Count > _targetFrameTimes.Count )
			{
				int last = _currentFrameTimes.Count - 1;
				if ( _currentFrameTimes[last] >= 0.999f )
				{
					_currentFrameTimes.RemoveAt( last );
					if ( _splatColorCache.Count > _targetFrameTimes.Count )
						_splatColorCache.RemoveAt( _splatColorCache.Count - 1 );
				}
				else break;
			}
		}

		ApplyGradientFromFrames();

		// Force the gradient widget to repaint this frame so the motion is smooth
		if ( _gradientControlWidget != null && _gradientControlWidget.IsValid() )
			_gradientControlWidget.Update();

		// The overlay mesh samples the live gradient, so rebuild it while the palette animates
		BuildOverlayMesh();

		if ( maxDelta < 0.001f )
		{
			_currentFrameTimes = _targetFrameTimes.ToList();
			_gradientAnimating = false;
		}
	}

	static Color RandomBrightColor()
	{
		// Pick a hue at random, keep saturation/value high so it stands out
		float hue = Random.Shared.NextSingle() * 360f;
		return new ColorHsv( hue, 0.8f, 1.0f ).ToColor();
	}

	void SetWidgetsVisible( List<Widget> widgets, bool visible )
	{
		foreach ( var widget in widgets )
		{
			widget.Visible = visible;
			widget.Enabled = visible;
		}
	}

	Widget CreatePropsPage()
	{
		var page = new Widget( null );
		page.VerticalSizeMode = SizeMode.CanShrink;
		page.HorizontalSizeMode = SizeMode.Flexible;
		page.Layout = Layout.Column();
		page.Layout.Margin = 10;
		page.Layout.Spacing = 5;
		page.Layout.Alignment = TextFlag.Top;
		return page;
	}

	FloatControlWidget FloatSlider( string propertyName )
	{
		var property = _serialized.GetProperty( propertyName );
		var control = new FloatControlWidget( property );
		MakeRanged( control, property );
		return control;
	}

	IntegerControlWidget IntSlider( string propertyName )
	{
		var property = _serialized.GetProperty( propertyName );
		var control = new IntegerControlWidget( property );
		MakeRanged( control, property );
		return control;
	}

	void MakeRanged( FloatControlWidget control, SerializedProperty property )
	{
		if ( property is null ) return;

		property.TryGetAttribute<MinMaxAttribute>( out var minMax );
		if ( minMax is null ) return;

		float step = 0.01f;
		if ( property.TryGetAttribute<StepAttribute>( out var stepAttr ) )
		{
			step = stepAttr.Step;
		}

		control.MakeRanged( new Vector2( minMax.MinValue, minMax.MaxValue ), step, true, true );
	}

	void RandomizeSeed()
	{
		var property = _serialized.GetProperty( nameof( TerrainSeed ) );
		if ( property is null ) return;

		TerrainSeed = Random.Shared.NextInt64();
		property.SetValue( TerrainSeed );
		_previewDirty = true;
	}

	void LoadFacepunchMaterialsAsync()
	{
		try
		{
			// Find all local tmat assets in the project's assets folder (not cloud ones).
			// Prefer 1K variants when a material has multiple resolutions.
			var allLocal = Editor.AssetSystem.All
				.Where( a => a is not null && !a.IsDeleted && !a.IsCloud )
				.Where( a => (a.RelativePath?.EndsWith( ".tmat" ) ?? false) )
				.ToList();

			var with1k = allLocal.Where( a => a.RelativePath.Contains( "_1k" ) ).ToList();

			_localTmatAssets = with1k.Count > 0 ? with1k : allLocal;

			if ( _localTmatAssets.Count == 0 )
				Log.Warning( "No local .tmat terrain materials found in the project's assets folder" );
		}
		catch ( System.Exception e )
		{
			Log.Error( $"Failed to find local terrain materials: {e.Message}" );
		}
	}

	void RandomizeMaterials()
	{
		if ( !PreviewSplatMaterials ) return;

		if ( _localTmatAssets is null || _localTmatAssets.Count == 0 )
		{
			// Load the local tmat list first, then randomize once it's available
			_ = LoadFacepunchMaterialsAndRandomize();
			return;
		}

		RandomizeMaterialsAsync();
	}

	async Task LoadFacepunchMaterialsAndRandomize()
	{
		try
		{
			// Find all local tmat assets in the project's assets folder (not cloud ones).
			// Prefer 1K variants when a material has multiple resolutions.
			var allLocal = Editor.AssetSystem.All
				.Where( a => a is not null && !a.IsDeleted && !a.IsCloud )
				.Where( a => (a.RelativePath?.EndsWith( ".tmat" ) ?? false) )
				.ToList();

			var with1k = allLocal.Where( a => a.RelativePath.Contains( "_1k" ) ).ToList();

			_localTmatAssets = with1k.Count > 0 ? with1k : allLocal;

			if ( PreviewSplatMaterials && _localTmatAssets.Count > 0 )
				RandomizeMaterialsAsync();
		}
		catch ( System.Exception e )
		{
			Log.Error( $"Failed to find local terrain materials: {e.Message}" );
		}
	}

	async void RandomizeMaterialsAsync()
	{
		if ( _materialLoadingLabel != null )
		{
			_materialLoadingLabel.Text = "Loading materials...";
			_materialLoadingLabel.Visible = true;
		}

		int layerCount = Math.Max( SplatLayerCount, 2 );
		int gen = ++_previewMaterialsGeneration;

		try
		{
			var pool = new List<Editor.Asset>( _localTmatAssets );

			var materials = new List<TerrainMaterial>();

			// Pull from the pool until we have layerCount usable materials (skipping any
			// whose textures fail to compile) or the pool runs out.
			while ( materials.Count < layerCount && pool.Count > 0 )
			{
				int idx = Random.Shared.Next( pool.Count );
				var asset = pool[idx];
				pool.RemoveAt( idx );

				if ( !asset.TryLoadResource<TerrainMaterial>( out var found ) || found is null )
				{
					Log.Warning( $"Failed to load TerrainMaterial from '{asset.Path}'" );
					continue;
				}

				// Only accept materials whose compiled BCR/NHO textures actually exist -
				// otherwise the terrain renders a pink checkerboard.
				if ( !IsMaterialUsable( found, asset.Path ) )
					continue;

				materials.Add( found );
			}

			if ( gen != _previewMaterialsGeneration ) return;

			if ( materials.Count == 0 )
			{
				_previewMaterials = null;
				Log.Warning( "No local terrain materials could be loaded" );
			}
			else
			{
				_previewMaterials = materials.ToArray();
			}

			_previewDirty = true;
		}
		catch ( System.Exception e )
		{
			Log.Error( $"Failed to load preview materials: {e.Message}" );
		}
		finally
		{
			if ( gen == _previewMaterialsGeneration && _materialLoadingLabel != null )
				_materialLoadingLabel.Visible = false;
		}
	}

	/// <summary>
	/// Checks that a terrain material's compiled BCR/NHO textures are usable. The terrain shader
	/// samples these bindlessly, and a missing/failed compile shows up as a pink checkerboard.
	/// </summary>
	bool IsMaterialUsable( TerrainMaterial material, string ident )
	{
		if ( material is null ) return false;

		try
		{
			var bcr = material.BCRTexture;
			if ( bcr is null || bcr.IsError || !bcr.IsValid )
			{
				Log.Warning( $"Skipping '{ident}': BCR texture missing or failed to compile" );
				return false;
			}

			var nho = material.NHOTexture;
			if ( nho is null || nho.IsError || !nho.IsValid )
			{
				Log.Warning( $"Skipping '{ident}': NHO texture missing or failed to compile" );
				return false;
			}

			return true;
		}
		catch ( System.Exception e )
		{
			Log.Warning( $"Skipping '{ident}': {e.Message}" );
			return false;
		}
	}

	void AddPropsTab( string name, string icon, Widget page, string tooltip )
	{
		_propsTabBar.AddOption( name, icon );
		_propsPages[name] = page;
		_propsContent.Layout.Add( page );

		page.Visible = false;
		page.ToolTip = tooltip;

		if ( _propsPages.Count == 1 )
		{
			SelectPropsTab( name );
		}
	}

	void SelectPropsTab( string name )
	{
		foreach ( var entry in _propsPages )
		{
			entry.Value.Visible = entry.Key == name;
		}

		if ( _activePropsTab != name )
		{
			_activePropsTab = name;
			_previewDirty = true;
		}
	}

	void ApplyConditionalVisibility()
	{
		SetWidgetsVisible( _domainWarpingWidgets, DomainWarping );
		SetWidgetsVisible( _riverCarvingWidgets, RiverCarvingBool );
		SetWidgetsVisible( _stagingAreaWidgets, StagingArea );
	}

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		// Tick the preview scene so the terrain clipmap builds and updates
		if ( RenderCanvas != null && RenderCanvas.Scene.IsValid() )
			RenderCanvas.Scene.EditorTick( RealTime.Now, RealTime.Delta );

		// Morph the overlay mesh toward its target shape every frame
		UpdateOverlayAnimation();

		// Animate the gradient color stops sliding in/out
		UpdateGradientAnimation();

		if ( !_previewDirty ) return;
		if ( RealTime.Now - _lastPreviewRegen < 0.1f ) return;
		if ( _isGenerating ) return;

		_previewDirty = false;
		_lastPreviewRegen = RealTime.Now;

		RegeneratePreviewAsync();
	}

	async void RegeneratePreviewAsync()
	{
		if ( _previewTerrain is null || !_previewTerrain.IsValid() ) return;
		if ( string.IsNullOrEmpty( CategoryArray?.Selected ) || string.IsNullOrEmpty( ShapeArray?.Selected ) ) return;

		if ( _isGenerating ) return;
		_isGenerating = true;
		int token = ++_generationToken;

		// Splat colors are read on the main thread into the shared arrays
		BuildSplatColors();

		// Snapshot UI-driven values on the main thread so the background task doesn't touch widgets
		string category = CategoryArray.Selected;
		string shape = ShapeArray.Selected;
		long seed = TerrainSeed;
		int noiseLayers = NoiseLayerStacks;
		float minHeight = TerrainMinHeight;
		float maxHeight = TerrainMaxHeight;
		bool warp = DomainWarping;
		float warpSize = DomainWarpingSize;
		float warpStrength = DomainWarpingStrength;
		int smoothing = SmoothingPasses;
		float planeScale = TerrainPlaneScale;
		bool rivers = RiverCarvingBool;
		float riverFrequency = RiverCarvingFrequency;
		float riverWidth = RiverCarvingWidth;
		float riverDepth = RiverCarvingDepth;
		float riverTurbFreq = RiverCarvingTurbulenceFrequency;
		float riverTurbStrength = RiverCarvingTurbulenceStrength;
		float riverSpacing = RiverCarvingSpacing;
		bool staging = StagingArea;
		int stagingSize = StagingAreaSize;
		float stagingHeight = StagingAreaHeight;
		float stagingX = StagingAreaX;
		float stagingY = StagingAreaY;

		try
		{
			// CPU-heavy work (noise, smoothing, rivers, splatmap) runs off the main thread
			float[,] heightmap = await Task.Run( () => BuildHeightmap(
				PreviewResolution, PreviewResolution,
				category, shape,
				seed, noiseLayers, minHeight, maxHeight,
				warp, warpSize, warpStrength,
				smoothing, planeScale,
				rivers, riverFrequency, riverWidth, riverDepth,
				riverTurbFreq, riverTurbStrength, riverSpacing,
				staging, stagingSize, stagingHeight, stagingX, stagingY ) );

			if ( token != _generationToken ) return;
			if ( heightmap is null ) return;

			_previewHeightmap = heightmap;

			// GPU/scene updates must happen back on the main thread
			UpdatePreviewTerrain( heightmap );
		}
		finally
		{
			_isGenerating = false;

			// If more changes came in while we were busy, regenerate again
			if ( _previewDirty && token == _generationToken )
			{
				_previewDirty = false;
				RegeneratePreviewAsync();
			}
		}
	}

	void OnPreFrame()
	{
		GizmoInstance.Input.IsHovered = IsActiveWindow && RenderCanvas.IsUnderMouse;

		var isAltHeld = Editor.Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt );
		var isLeftDown = Editor.Application.MouseButtons.HasFlag( MouseButtons.Left );

		var isInteracting = false;

		if ( GizmoInstance.OrbitCamera( Camera, RenderCanvas, ref _orbitDistance ) )
		{
			// User is manually orbiting - don't auto-spin this frame
			isInteracting = true;
			GizmoInstance.Input.IsHovered = false;
		}
		else if ( isAltHeld )
		{
			isInteracting = true;
		}
		else if ( isLeftDown && GizmoInstance.Input.IsHovered )
		{
			// Click and drag in the preview to adjust pitch/yaw
			isInteracting = true;

			var delta = Editor.Application.CursorDelta * 0.1f;

			_orbitPitch = Math.Clamp( _orbitPitch + delta.y, 5f, 85f );
			_orbitAngle += delta.x;
			if ( _orbitAngle >= 360f ) _orbitAngle -= 360f;
			if ( _orbitAngle < 0f ) _orbitAngle += 360f;

			PositionCameraForOrbit();
		}

		if ( !isInteracting && _autoSpin )
		{
			// Slowly rotate the camera around the terrain
			_orbitAngle += SpinSpeed * RealTime.Delta;
			if ( _orbitAngle >= 360f ) _orbitAngle -= 360f;
			PositionCameraForOrbit();
		}

		// Scroll wheel over the preview controls zoom
		if ( !isAltHeld && GizmoInstance.Input.IsHovered && MathF.Abs( Editor.Application.MouseWheelDelta.y ) > 0.001f )
		{
			var wheelDelta = Editor.Application.MouseWheelDelta.y;
			ZoomSlider.Value = Math.Clamp( ZoomSlider.Value + wheelDelta * 500f, ZoomSlider.Minimum, ZoomSlider.Maximum );
			UpdateOrbitFromZoom();
		}

		RenderCanvas.UpdateGizmoInputs( GizmoInstance.Input.IsHovered );
	}

	void PositionCameraForOrbit()
	{
		if ( Camera is null || !Camera.IsValid() ) return;

		float pitch = _orbitPitch;
		float yaw = _orbitAngle;

		var offset = new Vector3(
			MathF.Sin( MathX.DegreeToRadian( yaw ) ) * MathF.Cos( MathX.DegreeToRadian( pitch ) ),
			MathF.Cos( MathX.DegreeToRadian( yaw ) ) * MathF.Cos( MathX.DegreeToRadian( pitch ) ),
			MathF.Sin( MathX.DegreeToRadian( pitch ) )
		) * _orbitDistance;

		Camera.WorldPosition = Vector3.Zero + offset;
		Camera.WorldRotation = Rotation.LookAt( (Vector3.Zero - offset).Normal, Vector3.Up );
	}

	void UpdateOrbitFromZoom()
	{
		// Higher slider value = closer to terrain (zoom in)
		_orbitDistance = ZoomSlider.Maximum + ZoomSlider.Minimum - ZoomSlider.Value;
		PositionCameraForOrbit();
	}

	void BuildSplatColors()
	{
		// Build evenly spaced thresholds and sample the gradient for each layer color
		int layerCount = Math.Max( SplatLayerCount, 2 );

		var _splatthresholdtime = new List<float>();
		var _splatmapgradients = new List<SKColor>();
		for ( int i = 0; i < layerCount; i++ )
		{
			float t = layerCount <= 1 ? 0f : (float)i / (layerCount - 1);
			_splatthresholdtime.Add( t );

			var color = SplatMapGradient.Evaluate( t ).ToColor32();
			_splatmapgradients.Add( new SKColor( color.r, color.g, color.b, color.a ) );
		}
		_splatthresholds = _splatthresholdtime.ToArray();
		_splatcolors = _splatmapgradients.ToArray();
	}

	void RegeneratePreview()
	{
		if ( _previewTerrain is null || !_previewTerrain.IsValid() ) return;
		if ( string.IsNullOrEmpty( CategoryArray?.Selected ) || string.IsNullOrEmpty( ShapeArray?.Selected ) ) return;

		BuildSplatColors();

		var heightmap = BuildHeightmap(
			PreviewResolution, PreviewResolution,
			CategoryArray.Selected, ShapeArray.Selected,
			TerrainSeed, NoiseLayerStacks, TerrainMinHeight, TerrainMaxHeight,
			DomainWarping, DomainWarpingSize, DomainWarpingStrength,
			SmoothingPasses, TerrainPlaneScale,
			RiverCarvingBool, RiverCarvingFrequency, RiverCarvingWidth, RiverCarvingDepth,
			RiverCarvingTurbulenceFrequency, RiverCarvingTurbulenceStrength, RiverCarvingSpacing,
			StagingArea, StagingAreaSize, StagingAreaHeight, StagingAreaX, StagingAreaY );
		if ( heightmap is null ) return;

		_previewHeightmap = heightmap;
		UpdatePreviewTerrain( heightmap );
	}

	void UpdatePreviewTerrain( float[,] heightmap )
	{
		if ( _previewTerrain is null || !_previewTerrain.IsValid() ) return;
		if ( _previewStorage is null ) return;

		int res = heightmap.GetLength( 0 );

		// Write the heightmap into the terrain storage (0..65535 maps across TerrainHeight)
		ushort[] heightArray = new ushort[res * res];
		for ( int y = 0; y < res; y++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				float h = Math.Clamp( heightmap[x, y], 0f, 1f );
				heightArray[y * res + x] = (ushort)Math.Clamp( (int)(h * 65535f), 0, 65535 );
			}
		}
		_previewStorage.HeightMap = heightArray;

		// Build the control map (which materials go where). When the material preview is
		// enabled and we have assigned bluedock materials, blend them by the splat map
		// exactly like the exported terrain would. Otherwise use the single default material.
		uint[] controlMap = new uint[res * res];

		bool useMaterials = _activePropsTab == "Splat" && PreviewSplatMaterials && _previewMaterials != null && _previewMaterials.Length > 0;

		if ( useMaterials )
		{
			float[,] splatmap = GenerateSplatmap( heightmap, _splatthresholds, TerrainMaxHeight, SplatLayerCount, SplatDispersion, SplatBlendStrength );

			int matCount = _previewMaterials.Length;
			for ( int y = 0; y < res; y++ )
			{
				for ( int x = 0; x < res; x++ )
				{
					float layerPos = Math.Clamp( splatmap[x, y], 0f, matCount - 1f );
					int baseId = (int)MathF.Floor( layerPos );
					int overlayId = Math.Min( baseId + 1, matCount - 1 );
					byte blend = (byte)Math.Clamp( (int)((layerPos - baseId) * 255f), 0, 255 );

					controlMap[y * res + x] = new CompactTerrainMaterial( (byte)baseId, (byte)overlayId, blend, false ).Packed;
				}
			}
		}
		else
		{
			// Default: single material, no blending
			for ( int i = 0; i < controlMap.Length; i++ )
			{
				controlMap[i] = new CompactTerrainMaterial( 0, 0, 0, false ).Packed;
			}
		}

		_previewStorage.ControlMap = controlMap;

		// Assign the materials and push everything to the GPU
		if ( _previewMaterials != null )
		{
			_previewStorage.Materials.Clear();
			_previewStorage.Materials.AddRange( _previewMaterials );
		}

		// Only the terrain shows when we're on the Splat tab previewing the real materials.
		// The terrain must be enabled before touching its GPU state, otherwise SyncGPUTexture
		// throws - so sync only when it's going to be visible.
		if ( _previewTerrain != null ) _previewTerrain.Enabled = useMaterials;

		if ( useMaterials && _previewTerrain != null && _previewTerrain.IsValid() )
		{
			_previewTerrain.Create();
			_previewTerrain.SyncGPUTexture();
			_previewTerrain.UpdateMaterialsBuffer();
		}

		// Overlay logic: on the Splat tab with the material preview off, overlay the splatmap
		// colors so you can see the layer layout. On any other tab, overlay the original
		// height-based color we used to paint the mesh. When materials are previewing, no overlay.
		bool showSplatOverlay = _activePropsTab == "Splat" && !useMaterials;
		UpdateSplatOverlay( heightmap, !useMaterials, showSplatOverlay );
	}

	void UpdateSplatOverlay( float[,] heightmap, bool visible, bool splatColors )
	{
		if ( _splatOverlayRenderer is null || !_splatOverlayRenderer.IsValid() ) return;

		int res = heightmap.GetLength( 0 );

		// Store the target heightmap and the desired colors. The mesh itself is animated
		// toward this target in FrameUpdate so changes morph smoothly instead of snapping.
		if ( _overlayTargetHeights == null || _overlayTargetHeights.Length != res * res )
		{
			_overlayTargetHeights = new float[res * res];
			_overlayCurrentHeights = new float[res * res];
		}

		bool first = _splatOverlayRenderer.Model is null;

		for ( int y = 0; y < res; y++ )
		{
			for ( int x = 0; x < res; x++ )
			{
				_overlayTargetHeights[y * res + x] = Math.Clamp( heightmap[x, y], 0f, 1f );
			}
		}

		// If we've never built the mesh, snap to the current values so the first frame is correct.
		if ( first )
		{
			Array.Copy( _overlayTargetHeights, _overlayCurrentHeights, _overlayTargetHeights.Length );
			_overlayAnimating = false;
		}
		else
		{
			_overlayAnimating = true;
		}

		// Cache the splatmap (only changes when the heightmap regenerates). The vertex colors
		// are evaluated from the LIVE gradient each frame so they stay in sync with the widget.
		_overlayUseSplatColors = splatColors;
		if ( splatColors )
			_overlaySplatmap = GenerateSplatmap( heightmap, _splatthresholds, TerrainMaxHeight, SplatLayerCount, SplatDispersion, SplatBlendStrength );

		if ( first )
			BuildOverlayMesh();

		// Toggle visibility
		if ( _splatOverlayGO != null ) _splatOverlayGO.Enabled = visible;
	}

	/// <summary>
	/// Called every frame - eases the overlay mesh from its current shape toward the target shape.
	/// </summary>
	void UpdateOverlayAnimation()
	{
		if ( _splatOverlayRenderer is null || !_splatOverlayRenderer.IsValid() ) return;
		if ( !_overlayAnimating || _overlayTargetHeights is null || _overlayCurrentHeights is null ) return;

		int count = _overlayTargetHeights.Length;
		if ( _overlayCurrentHeights.Length != count ) return;

		// Exponential approach - fast at first, settles smoothly
		float t = 1f - MathF.Exp( -PreviewMorphSpeed * RealTime.Delta );

		float maxDelta = 0f;
		for ( int i = 0; i < count; i++ )
		{
			float delta = _overlayTargetHeights[i] - _overlayCurrentHeights[i];
			_overlayCurrentHeights[i] += delta * t;
			maxDelta = MathF.Max( maxDelta, MathF.Abs( delta ) );
		}

		// Rebuild the mesh from the current (eased) heights. Colors are sampled from the
		// live gradient inside BuildOverlayMesh so they animate as smoothly as the widget.
		BuildOverlayMesh();

		// Stop once we're close enough
		if ( maxDelta < 0.001f )
		{
			Array.Copy( _overlayTargetHeights, _overlayCurrentHeights, count );
			_overlayAnimating = false;
		}
	}

	void BuildOverlayMesh()
	{
		if ( _splatOverlayRenderer is null || !_splatOverlayRenderer.IsValid() ) return;
		if ( _overlayCurrentHeights is null ) return;

		int res = (int)MathF.Sqrt( _overlayCurrentHeights.Length );
		int vertexCount = res * res;

		const float worldSize = PreviewTerrainSize;
		const float worldHeight = PreviewTerrainHeight;
		float cellX = worldSize / res;
		float cellY = worldSize / res;

		// Color ease factor - the mesh morphs at half the gradient speed so the color
		// swipe across the terrain is smoother. First build snaps immediately.
		float colorT = _overlayCurrentColors is null ? 1f : 1f - MathF.Exp( -MeshMorphSpeed * RealTime.Delta );

		var vertices = new Vertex[vertexCount];
		var colors = _overlayCurrentColors ?? new Color32[vertexCount];

		bool useSplat = _overlayUseSplatColors && _overlaySplatmap != null;

		Parallel.For( 0, res, y =>
		{
			for ( int x = 0; x < res; x++ )
			{
				int index = y * res + x;
				float h = _overlayCurrentHeights[index];

				// Local space - the overlay GO is parented to the centered terrain GO
				Vector3 position = new Vector3( x * cellX, y * cellY, h * worldHeight );

				float hL = _overlayCurrentHeights[y * res + Math.Max( x - 1, 0 )];
				float hR = _overlayCurrentHeights[y * res + Math.Min( x + 1, res - 1 )];
				float hD = _overlayCurrentHeights[Math.Max( y - 1, 0 ) * res + x];
				float hU = _overlayCurrentHeights[Math.Min( y + 1, res - 1 ) * res + x];

				float dx = (hR - hL) * worldHeight / (2.0f * cellX);
				float dy = (hU - hD) * worldHeight / (2.0f * cellY);

				Vector3 normal = new Vector3( -dx, -dy, 1.0f ).Normal;

				// Sample the target color from the LIVE gradient each frame, then ease the
				// mesh color toward it so the swipe lags behind the widget and looks smooth.
				Color targetColor;
				if ( useSplat )
				{
					float layerPos = Math.Clamp( _overlaySplatmap[x, y], 0f, SplatLayerCount - 1f );
					int layer0 = (int)MathF.Floor( layerPos );
					int layer1 = Math.Min( layer0 + 1, SplatLayerCount - 1 );
					float t = layerPos - layer0;

					Color c0 = SplatMapGradient.Evaluate( Math.Clamp( layer0 / (float)Math.Max( SplatLayerCount - 1, 1 ), 0f, 1f ) );
					Color c1 = SplatMapGradient.Evaluate( Math.Clamp( layer1 / (float)Math.Max( SplatLayerCount - 1, 1 ), 0f, 1f ) );
					targetColor = Color.Lerp( c0, c1, t );
				}
				else
				{
					// The original height-based material color we painted on the mesh
					targetColor = Color.Lerp( Color.FromBytes( 60, 90, 40 ), Color.FromBytes( 200, 185, 150 ), h );
				}

				var target32 = targetColor.ToColor32();
				var current = colors[index];

				byte r = (byte)MathX.LerpTo( current.r, target32.r, colorT );
				byte g = (byte)MathX.LerpTo( current.g, target32.g, colorT );
				byte b = (byte)MathX.LerpTo( current.b, target32.b, colorT );
				byte a = (byte)MathX.LerpTo( current.a, target32.a, colorT );
				colors[index] = new Color32( r, g, b, a );

				vertices[index] = new Vertex( position, normal, normal, new Vector4( 0, 0, 0, 1 ) );
				vertices[index].Color = colors[index];
			}
		} );

		_overlayCurrentColors = colors;

		// Build indices once - the grid topology never changes
		var indices = new List<int>();
		if ( _overlayMesh is null )
		{
			for ( int y = 0; y < res - 1; y++ )
			{
				for ( int x = 0; x < res - 1; x++ )
				{
					int a = x + y * res;
					int b = (x + 1) + y * res;
					int c = (x + 1) + (y + 1) * res;
					int d = x + (y + 1) * res;

					indices.Add( a );
					indices.Add( b );
					indices.Add( c );
					indices.Add( a );
					indices.Add( c );
					indices.Add( d );
				}
			}
		}

		if ( _overlayMesh is null || !_overlayMesh.IsValid() )
		{
			_overlayMesh = new Mesh( _splatOverlayRenderer.MaterialOverride );
			_overlayMesh.CreateVertexBuffer( vertices.Length, vertices );
			_overlayMesh.CreateIndexBuffer( indices.Count, indices );
			_overlayMesh.Bounds = BBox.FromPositionAndSize( new Vector3( worldSize * 0.5f, worldSize * 0.5f, worldHeight * 0.5f ), new Vector3( worldSize, worldSize, worldHeight ) );

			_splatOverlayRenderer.Model = Model.Builder.AddMesh( _overlayMesh ).Create();
		}
		else
		{
			// Update the existing vertex buffer in place - much faster than rebuilding the model
			_overlayMesh.SetVertexBufferData( vertices );
		}
	}

	float[,] BuildHeightmap( int width, int height,
		string category,
		string shape,
		long seed,
		int layerCount,
		float minHeight,
		float maxHeight,
		bool domainWarping,
		float domainWarpingSize,
		float domainWarpingStrength,
		int smoothingPasses,
		float terrainPlaneScale,
		bool riverCarving,
		float riverFrequency,
		float riverWidth,
		float riverDepth,
		float riverTurbulenceFrequency,
		float riverTurbulenceStrength,
		float minRiverSpacing,
		bool stagingArea,
		int stagingAreaSize,
		float stagingAreaHeight,
		float stagingAreaX,
		float stagingAreaY )
	{
		var fullclass = Type.GetType( $"Sturnus.TerrainGenerationTool.{category}" );
		if ( fullclass is null )
		{
			Log.Error( $"Class '{category}' not found." );
			return null;
		}
		var fullclassmethod = fullclass.GetMethod( shape );
		if ( fullclassmethod is null )
		{
			Log.Error( $"Method '{shape}' not found." );
			return null;
		}

		float[,] heightmap = GenerateStackedNoise(
			width,
			height,
			seed,
			layerCount,
			1.0f,
			2.0f,
			1.0f,
			0.5f,
			( x, y ) => (float)CallMethod( $"Sturnus.TerrainGenerationTool.{category}", shape, new object[] {
			x, y,
			width,
			height,
			seed,
			minHeight,
			domainWarping,
			domainWarpingSize,
			domainWarpingStrength
			} ),
			maxHeight,
			smoothingPasses,
			terrainPlaneScale
		);

		if ( ErosionSimulation )
		{

		}

		if ( riverCarving )
		{
			heightmap = AddTurbulenceForRivers(
			heightmap,
			seed: seed,
			riverFrequency: riverFrequency,
			riverWidth: riverWidth,
			riverDepth: riverDepth,
			turbulenceFrequency: riverTurbulenceFrequency,
			turbulenceStrength: riverTurbulenceStrength,
			minRiverSpacing: minRiverSpacing,
			slopeSteepness:10f,
			terrainNoiseFrequency: 2.0f,
			terrainNoiseAmplitude: 0.5f
		);
		}

		if ( heightmap == null )
		{
			Log.Error( "Heightmap is not generated. Aborting." );
			return null;
		}

		if ( stagingArea )
		{
			heightmap = AddStagingSquare(
			heightmap,
			stagingAreaSize,
			stagingAreaHeight,
			stagingAreaX,
			stagingAreaY );
		}

		return heightmap;
	}

	public void RebuildShapes()
	{
		ShapeArray.DestroyChildren();
		TerrainShapeArray.Clear();

		if ( CategoryArray?.Selected is null )
		{
			return;
		}

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
		float[,] splatmap = GenerateSplatmap( _heightmap, _splatthresholds, TerrainMaxHeight, SplatLayerCount, SplatDispersion, SplatBlendStrength );
		SKBitmap splat = SplatmapToBitMap( splatmap, _splatcolors );
		SaveSplatmapAsPng( splat, splatfile );
		Log.Info( $"Splatmap file generated! - {splatfile}" );

		// Split the layers across the requested number of splat maps
		int layerCount = Math.Max( SplatLayerCount, 2 );
		int mapCount = Math.Max( SplatMapCount, 1 );
		for ( int m = 0; m < mapCount; m++ )
		{
			int startLayer = m * layerCount / mapCount;
			int endLayer = (m + 1) * layerCount / mapCount;

			var mapBitmap = new SKBitmap( splatmap.GetLength( 0 ), splatmap.GetLength( 1 ) );

			for ( int y = 0; y < mapBitmap.Height; y++ )
			{
				for ( int x = 0; x < mapBitmap.Width; x++ )
				{
					float layerPos = Math.Clamp( splatmap[x, y], 0f, layerCount - 1f );
					int layer = (int)MathF.Round( layerPos );

					if ( layer >= startLayer && layer < endLayer )
					{
						float local = (layer - startLayer) / (float)Math.Max( endLayer - startLayer, 1 );
						var color = SplatMapGradient.Evaluate( Math.Clamp( local, 0f, 1f ) ).ToColor32();
						mapBitmap.SetPixel( x, y, new SKColor( color.r, color.g, color.b, color.a ) );
					}
					else
					{
						mapBitmap.SetPixel( x, y, new SKColor( 0, 0, 0, 255 ) );
					}
				}
			}

			string mapFile = Path.Combine( output_path, $"TerrainGenerationUtility_splatmap_{m}_{/*TerrainShapeEnumSelect*/null}{UsingDomainWarping}{UsingErosionEmulation}{UsingWaterCarving}.png" );
			SaveSplatmapAsPng( mapBitmap, mapFile );
			Log.Info( $"Splatmap {m} file generated! - {mapFile}" );
		}

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
				// (each Parallel.For iteration writes its own distinct row - no lock needed)
				heightmap[x, y] += value;
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
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		float max = float.MinValue;
		object maxLock = new object();

		Parallel.For( 0, height, y =>
		{
			float rowMax = float.MinValue;
			for ( int x = 0; x < width; x++ )
			{
				if ( heightmap[x, y] > rowMax )
				{
					rowMax = heightmap[x, y];
				}
			}

			if ( rowMax > max )
			{
				lock ( maxLock )
				{
					if ( rowMax > max ) max = rowMax;
				}
			}
		} );

		return max;
	}


	private static float[,] NormalizeHeightmap( float[,] heightmap )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		// Find the min and max values (parallel, per-row reduce)
		object lockObject = new object();
		float min = float.MaxValue;
		float max = float.MinValue;

		Parallel.For( 0, height, y =>
		{
			float rowMin = float.MaxValue;
			float rowMax = float.MinValue;
			for ( int x = 0; x < width; x++ )
			{
				float value = heightmap[x, y];
				if ( value < rowMin ) rowMin = value;
				if ( value > rowMax ) rowMax = value;
			}

			lock ( lockObject )
			{
				if ( rowMin < min ) min = rowMin;
				if ( rowMax > max ) max = rowMax;
			}
		} );

		float range = max - min;
		if ( range <= 0f ) range = 1f;

		// Normalize the values (parallel, independent writes)
		float[,] normalized = new float[width, height];
		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				normalized[x, y] = (heightmap[x, y] - min) / range;
			}
		} );

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
		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				float nx = x / (float)width;
				float ny = y / (float)height;

				// Noise for river placement
				riverPlacementNoise[x, y] = OpenSimplex2S.Noise2( seed, nx * riverFrequency, ny * riverFrequency );
			}
		} );

		// Process heightmap with river carving
		Parallel.For( 0, height, y =>
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
		} );

		// Add base noise to the entire heightmap after carving
		Parallel.For( 0, height, y =>
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
		} );

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
			Parallel.For( 0, height, y =>
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
			} );

			// Copy smoothed values back to the original heightmap for the next pass
			Parallel.For( 0, height, y =>
			{
				for ( int x = 0; x < width; x++ )
				{
					heightmap[x, y] = smoothed[x, y];
				}
			} );
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

	public static float[,] GenerateSplatmap( float[,] heightmap, float[] thresholds, float maxHeight, int layerCount = -1, SplatDispersionMode dispersion = SplatDispersionMode.Evenly, float blendStrength = 0.35f )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		int layers = layerCount > 0 ? layerCount : Math.Max( thresholds.Length, 2 );
		float[,] splatmap = new float[width, height];

		// Build normalized height bounds from the data itself (robust to maxHeight being lower than peaks)
		float minH = float.MaxValue, maxH = float.MinValue;
		object minMaxLock = new object();

		Parallel.For( 0, height, y =>
		{
			float rowMin = float.MaxValue;
			float rowMax = float.MinValue;
			for ( int x = 0; x < width; x++ )
			{
				if ( heightmap[x, y] < rowMin ) rowMin = heightmap[x, y];
				if ( heightmap[x, y] > rowMax ) rowMax = heightmap[x, y];
			}

			lock ( minMaxLock )
			{
				if ( rowMin < minH ) minH = rowMin;
				if ( rowMax > maxH ) maxH = rowMax;
			}
		} );

		float range = MathF.Max( maxH - minH, 0.0001f );

		// Thresholds are the height positions of each color stop in [0,1].
		// Evenly mode uses equally spaced stops; Natural mode uses a slope-weighted
		// distribution so colors bunch on flat/common terrain and spread on steep slopes.
		float[] stops = thresholds;
		if ( dispersion == SplatDispersionMode.Natural )
		{
			stops = ComputeNaturalThresholds( heightmap, layers );
		}

		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				// Normalized height in [0,1]
				float normalizedHeight = Math.Clamp( (heightmap[x, y] - minH) / range, 0f, 1f );

				// Interpolated layer position from the color stop positions
				float layerPos = HeightToLayer( normalizedHeight, stops );

				// Soft snap to the nearest layer governed by blend strength
				float center = MathF.Round( layerPos );
				float distance = layerPos - center;

				float factor;
				if ( MathF.Abs( distance ) <= blendStrength * 0.5f )
				{
					factor = layerPos;
				}
				else
				{
					factor = center;
				}

				splatmap[x, y] = Math.Clamp( factor, 0f, layers - 1 );
			}
		} );

		return splatmap;
	}

	static float HeightToLayer( float normalizedHeight, float[] stops )
	{
		int count = stops.Length;
		if ( count <= 1 ) return 0f;
		if ( normalizedHeight <= stops[0] ) return 0f;
		if ( normalizedHeight >= stops[count - 1] ) return count - 1f;

		for ( int i = 0; i < count - 1; i++ )
		{
			if ( normalizedHeight >= stops[i] && normalizedHeight <= stops[i + 1] )
			{
				float t = (normalizedHeight - stops[i]) / MathF.Max( stops[i + 1] - stops[i], 0.0001f );
				return i + t;
			}
		}

		return count - 1f;
	}

	/// <summary>
	/// Places color stop thresholds based on the terrain's slope-weighted height distribution.
	/// Flat, common heights get many stops (lots of color blending); steep, rare heights get few
	/// stops (few color changes), matching how terrain materials naturally appear.
	/// </summary>
	static float[] ComputeNaturalThresholds( float[,] heightmap, int layerCount )
	{
		int width = heightmap.GetLength( 0 );
		int height = heightmap.GetLength( 1 );

		float minH = float.MaxValue, maxH = float.MinValue;
		object minMaxLock = new object();

		Parallel.For( 0, height, y =>
		{
			float rowMin = float.MaxValue;
			float rowMax = float.MinValue;
			for ( int x = 0; x < width; x++ )
			{
				if ( heightmap[x, y] < rowMin ) rowMin = heightmap[x, y];
				if ( heightmap[x, y] > rowMax ) rowMax = heightmap[x, y];
			}

			lock ( minMaxLock )
			{
				if ( rowMin < minH ) minH = rowMin;
				if ( rowMax > maxH ) maxH = rowMax;
			}
		} );
		float range = MathF.Max( maxH - minH, 0.0001f );

		// Histogram of normalized heights, weighted by flatness (1 - slope).
		// Use a per-thread local histogram, then merge, to avoid lock contention.
		const int bins = 128;
		float[] hist = new float[bins];

		Parallel.For( 0, height, y =>
		{
			float[] localHist = new float[bins];

			for ( int x = 0; x < width; x++ )
			{
				float h = heightmap[x, y];

				float hL = heightmap[Math.Max( x - 1, 0 ), y];
				float hR = heightmap[Math.Min( x + 1, width - 1 ), y];
				float hD = heightmap[x, Math.Max( y - 1, 0 )];
				float hU = heightmap[x, Math.Min( y + 1, height - 1 )];

				float localDiff = (MathF.Abs( hR - hL ) + MathF.Abs( hU - hD )) * 0.5f;
				float slope = Math.Clamp( localDiff / MathF.Max( range * 0.1f, 0.0001f ), 0f, 1f );

				float weight = MathF.Max( 1f - slope, 0.05f );
				float normalizedHeight = Math.Clamp( (h - minH) / range, 0f, 1f );
				int bin = Math.Clamp( (int)(normalizedHeight * (bins - 1)), 0, bins - 1 );
				localHist[bin] += weight;
			}

			lock ( minMaxLock )
			{
				for ( int i = 0; i < bins; i++ ) hist[i] += localHist[i];
			}
		} );

		// Cumulative distribution
		float total = hist.Sum();
		if ( total <= 0f )
		{
			total = 1f;
			for ( int i = 0; i < bins; i++ ) hist[i] = 1f;
		}

		float[] thresholds = new float[layerCount];
		thresholds[0] = 0f;
		thresholds[layerCount - 1] = 1f;

		float cum = 0f;
		int binIndex = 0;
		for ( int i = 1; i < layerCount - 1; i++ )
		{
			float target = (i / (float)(layerCount - 1)) * total;
			while ( binIndex < bins - 1 && cum < target )
			{
				cum += hist[binIndex];
				binIndex++;
			}
			thresholds[i] = binIndex / (float)(bins - 1);
		}

		// Ensure monotonic
		for ( int i = 1; i < layerCount; i++ )
		{
			thresholds[i] = MathF.Max( thresholds[i], thresholds[i - 1] );
		}

		return thresholds;
	}

	public static SKBitmap SplatmapToBitMap( float[,] splatmap, SKColor[] colors )
	{
		int width = splatmap.GetLength( 0 );
		int height = splatmap.GetLength( 1 );
		SKBitmap bitmap = new SKBitmap( width, height );

		Parallel.For( 0, height, y =>
		{
			for ( int x = 0; x < width; x++ )
			{
				// Map the splatmap value to a valid layer position
				float layerPos = Math.Clamp( splatmap[x, y], 0f, colors.Length - 1f );
				int layer0 = (int)MathF.Floor( layerPos );
				int layer1 = Math.Min( layer0 + 1, colors.Length - 1 );
				float t = layerPos - layer0;

				// Blend between the two nearest layer colors
				var c0 = colors[layer0];
				var c1 = colors[layer1];
				var color = new SKColor(
					(byte)MathX.LerpTo( c0.Red, c1.Red, t ),
					(byte)MathX.LerpTo( c0.Green, c1.Green, t ),
					(byte)MathX.LerpTo( c0.Blue, c1.Blue, t ),
					(byte)MathX.LerpTo( c0.Alpha, c1.Alpha, t ) );

				bitmap.SetPixel( x, y, color );
			}
		} );

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

/// <summary>
/// An icon + label picker whose options wrap onto multiple lines.
/// Mimics the interface of <see cref="Editor.SegmentedControl"/> (AddOption, Selected, SelectedIndex, OnSelectedChanged).
/// </summary>
public class WrapSelector : Widget
{
	readonly List<WrapOption> _buttons = new();
	readonly List<string> _names = new();

	public string Selected
	{
		get
		{
			for ( int i = 0; i < _buttons.Count; i++ )
			{
				if ( _buttons[i].IsActive )
					return _names[i];
			}
			return null;
		}
		set
		{
			SetSelected( value );
		}
	}

	public int SelectedIndex
	{
		get
		{
			for ( int i = 0; i < _buttons.Count; i++ )
			{
				if ( _buttons[i].IsActive )
					return i;
			}
			return -1;
		}
		set
		{
			if ( value >= 0 && value < _names.Count )
				SetSelected( _names[value] );
		}
	}

	public Action<string> OnSelectedChanged { get; set; }

	public WrapSelector( Widget parent = null ) : base( parent )
	{
		Layout = Layout.Row();
		Layout.Spacing = 4;
		SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );
		HorizontalSizeMode = SizeMode.Flexible;
	}

	public void AddOption( string name, string icon = null, int? count = null, string label = null )
	{
		if ( _names.Contains( name ) ) return;

		if ( string.IsNullOrEmpty( name ) )
		{
			// Special "clear" option, shown with the close icon
			icon ??= "close";
		}
		else
		{
			icon ??= IconFor( name );
		}

		var option = new WrapOption( this, label ?? (string.IsNullOrEmpty( name ) ? "Clear" : name), icon );
		option.IsActive = false;
		option.MouseLeftPress = () => SetSelected( name );

		_names.Add( name );
		_buttons.Add( option );
		Layout.Add( option );
	}

	public bool HasOption( string name ) => _names.Contains( name );

	public new void DestroyChildren()
	{
		foreach ( var b in _buttons )
		{
			if ( b.IsValid() )
				b.Destroy();
		}
		_buttons.Clear();
		_names.Clear();
	}

	void SetSelected( string name )
	{
		bool changed = Selected != name;

		for ( int i = 0; i < _buttons.Count; i++ )
		{
			_buttons[i].IsActive = _names[i] == name;
		}

		if ( changed )
		{
			OnSelectedChanged?.Invoke( name );
		}
	}

	static string IconFor( string name )
	{
		switch ( name )
		{
			case "Islands": return "landscape";
			case "Mountainous": return "terrain";
			case "Planetary": return "public";
			case "Realistic": return "photo";
			case "Sea": return "water";
			case "Volcanic": return "volcano";
			case "Default": return "shapes";
			case "Archipelagos": return "scatter_plot";
			case "Atoll": return "crop_square";
			case "Islets": return "blur_on";
			case "Oceanic": return "waves";
			case "Cliff": return "terrain";
			case "Craters": return "brightness_low";
			case "Hills": return "landscape";
			case "Plateau": return "square_foot";
			case "SeaBed": return "water";
			case "Sharded": return "dashboard";
			default: return "shapes";
		}
	}
}

/// <summary>
/// A single option in a <see cref="WrapSelector"/>: an icon with a text label underneath.
/// </summary>
public class WrapOption : Widget
{
	public string Icon { get; }
	public string Text { get; }
	public bool IsActive { get; set; }

	public WrapOption( Widget parent, string text, string icon ) : base( parent )
	{
		Text = text;
		Icon = icon;
		Cursor = CursorShape.Finger;
		ToolTip = text;
		MinimumSize = new Vector2( 52, 44 );
	}

	protected override Vector2 SizeHint()
	{
		Paint.SetDefaultFont( 7 );
		var textRect = Paint.MeasureText( new Rect( 0, 0, 60, 100 ), Text, TextFlag.WordWrap );
		return new Vector2( MathF.Max( textRect.Size.x + 10, 52 ), textRect.Size.y + 24 );
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = true;
		Paint.ClearPen();

		var rect = LocalRect;

		var background = IsActive ? Theme.Primary.WithAlpha( 0.25f ) : Theme.ControlBackground.WithAlpha( 0.6f );
		if ( Paint.HasMouseOver ) background = background.Lighten( 0.1f );
		Paint.SetBrush( background );
		Paint.DrawRect( rect, Theme.ControlRadius );

		var iconRect = new Rect( rect.Left, rect.Top + 4, rect.Width, rect.Height * 0.55f );
		var color = IsActive ? Theme.Primary : Theme.Text.WithAlpha( 0.8f );
		Paint.SetPen( color );
		Paint.DrawIcon( iconRect, Icon, 18, TextFlag.Center );

		var textRect = new Rect( rect.Left + 2, rect.Top + rect.Height * 0.55f, rect.Width - 4, rect.Height * 0.45f );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( textRect, Text, TextFlag.Center | TextFlag.WordWrap );
	}
}

