using System;
using System.Collections.Generic;
using Editor;
using Editor.TerrainEditor;
using Sandbox;

namespace Sturnus.TerrainGenerationTool.EditorTools;

/// <summary>
/// A terrain editor tool that includes every stock brush from the built-in terrain tool,
/// plus any custom brushes defined in this library. Register your own brushes by returning
/// them from <see cref="GetSubtools"/>.
/// </summary>
[EditorTool]
[Title( "Terrain Pro" )]
[Icon( "landscape" )]
[Alias( "tools.terrain-pro" )]
[Group( "Scene" )]
public class ExtendedTerrainTool : TerrainEditorTool
{
	public override IEnumerable<EditorTool> GetSubtools()
	{
		// Stock brushes, minus the Hole tool (we don't want holes in Terrain Pro)
		foreach ( var tool in base.GetSubtools() )
		{
			if ( tool is HoleTool )
				continue;

			yield return tool;
		}

		// Custom brushes from this library
		yield return new BulgeBrushTool( this );
		yield return new CraterBrushTool( this );
		yield return new TerraceBrushTool( this );
		yield return new NoiseBrushTool( this );
	}
}

/// <summary>
/// Base for CPU-side sculpt brushes. The stock tools sculpt with a GPU compute shader;
/// these do the same on the CPU by directly editing the terrain's heightmap array, which
/// lets us invent entirely new brush behaviour without shipping a new shader.
/// Also keeps the painted region highlighted while the mouse is held down.
/// </summary>
public abstract class CpuSculptBrushTool : BaseBrushTool
{
	ushort[] _strokeBefore;
	RectInt _strokeRegion;
	bool _strokeActive;

	// Brush footprints stamped so far this stroke, for the circle highlight
	List<(int cx, int cy, int size)> _strokeCircles;

	// One terrain-projected decal per stamped circle, like the stock brush preview
	List<BrushPreviewSceneObject> _highlightObjects;

	// Deferred-apply selection: per-texel max falloff weight covered by the stroke
	float[] _selectionWeights;
	float _selectionOpacity;

	/// <summary>
	/// When true, painting only records the brush footprint (a selection) instead of sculpting.
	/// The whole selection is sculpted at once in <see cref="ApplySelection"/> when the mouse is
	/// released, so the effect lands across the entire dragged area simultaneously.
	/// </summary>
	protected virtual bool ApplyOnRelease => false;

	public override bool PaintMode { get; set; } = true;

	protected CpuSculptBrushTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		// Keep the brush circles we've painted highlighted until the mouse is released
		if ( _strokeActive )
		{
			var terrain = GetSelectedComponent<Terrain>() ?? Scene.Get<Terrain>();
			if ( terrain.IsValid() )
				UpdateStrokeHighlight( terrain );
		}
	}

	/// <summary>
	/// Renders a translucent brush-preview decal for every circle stamped during the stroke.
	/// Uses the same terrain-projected <see cref="BrushPreviewSceneObject"/> the stock terrain
	/// tool shows for its brush preview, so the highlight hugs the terrain surface.
	/// </summary>
	void UpdateStrokeHighlight( Terrain terrain )
	{
		int res = terrain.Storage.Resolution;
		if ( res <= 0 || _strokeCircles == null ) return;

		// Grow/shrink the decal list to match the number of stamped circles
		if ( _highlightObjects == null )
			_highlightObjects = new List<BrushPreviewSceneObject>();

		while ( _highlightObjects.Count < _strokeCircles.Count )
			_highlightObjects.Add( new BrushPreviewSceneObject( Gizmo.World ) );

		while ( _highlightObjects.Count > _strokeCircles.Count )
		{
			var extra = _highlightObjects[^1];
			extra.Delete();
			_highlightObjects.RemoveAt( _highlightObjects.Count - 1 );
		}

		var tx = terrain.WorldTransform;
		float heightScale = terrain.Storage.TerrainHeight / 65535f;
		float unitsPerTexel = terrain.Storage.TerrainSize / (float)res;

		for ( int i = 0; i < _strokeCircles.Count; i++ )
		{
			var (cx, cy, size) = _strokeCircles[i];

			int hx = Math.Clamp( cx, 0, res - 1 );
			int hy = Math.Clamp( cy, 0, res - 1 );
			float h = terrain.Storage.HeightMap[hy * res + hx] * heightScale;

			var obj = _highlightObjects[i];
			obj.RenderLayer = SceneRenderLayer.OverlayWithDepth;
			obj.Bounds = BBox.FromPositionAndSize( 0, float.MaxValue );
			obj.Transform = new Transform( tx.PointToWorld( new Vector3( cx * unitsPerTexel, cy * unitsPerTexel, h ) ), tx.Rotation );
			obj.Radius = size * 0.5f * unitsPerTexel;
			obj.Texture = TerrainEditorTool.Brush?.Texture;
			obj.Color = Color.FromBytes( 255, 165, 0 ).WithAlpha( 0.5f );
		}
	}

	/// <summary>
	/// Deletes the highlight decals, called when the stroke ends.
	/// </summary>
	void ClearStrokeHighlight()
	{
		if ( _highlightObjects != null )
		{
			foreach ( var obj in _highlightObjects )
				obj.Delete();
			_highlightObjects.Clear();
		}
	}

	protected override void OnPaint( Terrain terrain, TerrainPaintParameters paint )
	{
		int res = terrain.Storage.Resolution;

		// Brush footprint in texels
		int size = (int)Math.Floor( paint.BrushSettings.Size * 2.0f / terrain.Storage.TerrainSize * res );
		size = Math.Max( size, 1 );

		int cx = (int)Math.Floor( paint.HitUV.x * res );
		int cy = (int)Math.Floor( paint.HitUV.y * res );

		var region = new RectInt( cx - size / 2, cy - size / 2, size + 1, size + 1 );

		// On the first paint of a stroke, snapshot the entire heightmap so undo can restore it
		// no matter how far the stroke drags.
		if ( !_strokeActive )
		{
			_strokeActive = true;
			_strokeRegion = region;
			_strokeBefore = (ushort[])terrain.Storage.HeightMap.Clone();
			_strokeCircles = new List<(int, int, int)>();

			if ( ApplyOnRelease )
			{
				_selectionWeights = new float[res * res];
				_selectionOpacity = paint.BrushSettings.Opacity;
			}
		}
		else
		{
			// Expand the dirty region to cover this frame's footprint
			int left = Math.Min( _strokeRegion.Left, region.Left );
			int top = Math.Min( _strokeRegion.Top, region.Top );
			int right = Math.Max( _strokeRegion.Right, region.Right );
			int bottom = Math.Max( _strokeRegion.Bottom, region.Bottom );
			_strokeRegion = new RectInt( left, top, right - left, bottom - top );
		}

		_strokeCircles.Add( (cx, cy, size) );

		if ( ApplyOnRelease )
		{
			// Only mark the selection - no height changes until the mouse is released
			StampSelection( terrain, paint, res, cx, cy, size );
		}
		else
		{
			// Let the brush decide how to edit each texel
			Sculpt( terrain, paint, res, cx, cy, size );

			// Upload CPU -> GPU and refresh collision so the sculpt shows live
			terrain.SyncGPUTexture();
			terrain.UpdateCollision( Terrain.SyncFlags.Height, _strokeRegion );
		}
	}

	protected abstract void Sculpt( Terrain terrain, TerrainPaintParameters paint, int res, int centerX, int centerY, int size );

	/// <summary>
	/// Records the brush's falloff weight for every texel in the footprint. The strongest weight
	/// any stamp leaves on a texel wins, so overlapping circles blend into one clean selection.
	/// </summary>
	void StampSelection( Terrain terrain, TerrainPaintParameters paint, int res, int centerX, int centerY, int size )
	{
		int radius = size / 2;

		for ( int y = -radius; y <= radius; y++ )
		{
			for ( int x = -radius; x <= radius; x++ )
			{
				int tx = centerX + x;
				int ty = centerY + y;
				if ( tx < 0 || ty < 0 || tx >= res || ty >= res ) continue;

				float w = SampleBrush( paint, x + radius, y + radius, size );
				if ( w <= 0.001f ) continue;

				int index = ty * res + tx;
				if ( w > _selectionWeights[index] )
					_selectionWeights[index] = w;
			}
		}
	}

	/// <summary>
	/// Applies the brush over the whole recorded selection at once. Called on mouse release when
	/// <see cref="ApplyOnRelease"/> is true. The weights array holds the max falloff weight for
	/// every texel touched by the stroke.
	/// </summary>
	protected virtual void ApplySelection( Terrain terrain, int res, float[] weights, float opacity )
	{
	}

	protected override void OnPaintEnded( Terrain terrain )
	{
		if ( _strokeActive )
		{
			int res = terrain.Storage.Resolution;

			// Clamp the unioned dirty region to the terrain bounds
			_strokeRegion.Left = Math.Clamp( _strokeRegion.Left, 0, res - 1 );
			_strokeRegion.Right = Math.Clamp( _strokeRegion.Right, 0, res - 1 );
			_strokeRegion.Top = Math.Clamp( _strokeRegion.Top, 0, res - 1 );
			_strokeRegion.Bottom = Math.Clamp( _strokeRegion.Bottom, 0, res - 1 );

			if ( ApplyOnRelease && _selectionWeights != null )
			{
				// Sculpt the entire dragged selection in one go, then sync once
				ApplySelection( terrain, res, _selectionWeights, _selectionOpacity );
				terrain.SyncGPUTexture();
				terrain.UpdateCollision( Terrain.SyncFlags.Height, _strokeRegion );
			}

			ushort[] after = (ushort[])terrain.Storage.HeightMap.Clone();
			var region = _strokeRegion;

			Action Restore( ushort[] data ) => () =>
			{
				if ( !terrain.IsValid() ) return;
				WriteHeightRegion( terrain.Storage.HeightMap, res, region, data );
				terrain.SyncGPUTexture();
				terrain.UpdateCollision( Terrain.SyncFlags.Height, region );
			};

			SceneEditorSession.Active.UndoSystem.Insert( $"Terrain {DisplayInfo.For( this ).Name}", Restore( _strokeBefore ), Restore( after ) );

			_strokeBefore = null;
			_strokeActive = false;
			_strokeCircles = null;
			_selectionWeights = null;
			ClearStrokeHighlight();
		}
	}

	/// <summary>
	/// Sample the selected brush's falloff at a local footprint coordinate.
	/// Returns 0..1, 1 in the middle, 0 at the edges.
	/// </summary>
	protected float SampleBrush( TerrainPaintParameters paint, int localX, int localY, int size )
	{
		if ( size <= 0 ) return 0f;

		var pixmap = paint.Brush?.Pixmap;
		if ( pixmap != null && pixmap.Width > 0 && pixmap.Height > 0 )
		{
			float u = (localX + 0.5f) / size;
			float v = (localY + 0.5f) / size;

			int px = Math.Clamp( (int)(u * pixmap.Width), 0, pixmap.Width - 1 );
			int py = Math.Clamp( (int)(v * pixmap.Height), 0, pixmap.Height - 1 );

			var c = pixmap.GetPixel( px, py );
			return Math.Clamp( c.r, 0f, 1f );
		}

		// Fallback: soft radial falloff
		float nx = (localX + 0.5f - size * 0.5f) / (size * 0.5f);
		float ny = (localY + 0.5f - size * 0.5f) / (size * 0.5f);
		float d = MathF.Sqrt( nx * nx + ny * ny );
		return Math.Clamp( 1f - d, 0f, 1f );
	}

	/// <summary>
	/// Writes a full heightmap snapshot into the given dirty region. The snapshot may be a full
	/// map clone, but we only copy the region we actually painted so collision updates stay cheap.
	/// </summary>
	static void WriteHeightRegion( ushort[] heightmap, int res, RectInt region, ushort[] data )
	{
		for ( int y = 0; y < region.Height; y++ )
		{
			for ( int x = 0; x < region.Width; x++ )
			{
				heightmap[region.Left + x + (region.Top + y) * res] = data[region.Left + x + (region.Top + y) * res];
			}
		}
	}
}

/// <summary>
/// Raises a smooth dome inside the brush footprint.
/// </summary>
[Title( "Bulge" )]
[Icon( "bubble_chart" )]
[Alias( "tools.terrain.bulge" )]
[Group( "1" )]
[Order( 1 )]
public class BulgeBrushTool : CpuSculptBrushTool
{
	public BulgeBrushTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	protected override void Sculpt( Terrain terrain, TerrainPaintParameters paint, int res, int centerX, int centerY, int size )
	{
		var heightmap = terrain.Storage.HeightMap;
		float opacity = paint.BrushSettings.Opacity;
		int radius = size / 2;

		for ( int y = -radius; y <= radius; y++ )
		{
			for ( int x = -radius; x <= radius; x++ )
			{
				int tx = centerX + x;
				int ty = centerY + y;
				if ( tx < 0 || ty < 0 || tx >= res || ty >= res ) continue;

				float brush = SampleBrush( paint, x + radius, y + radius, size );
				if ( brush <= 0.001f ) continue;

				int index = ty * res + tx;
				float current = heightmap[index] / 65535f;

				// Parabolic dome: 1 at centre, 0 at the edge
				float dome = brush * brush;
				float target = current + dome * opacity;

				heightmap[index] = (ushort)Math.Clamp( target * 65535f, 0, 65535 );
			}
		}
	}
}

/// <summary>
/// Digs a rounded depression with a slightly raised rim, like an impact crater.
/// </summary>
[Title( "Crater" )]
[Icon( "brightness_low" )]
[Alias( "tools.terrain.crater" )]
[Group( "1" )]
[Order( 1 )]
public class CraterBrushTool : CpuSculptBrushTool
{
	public CraterBrushTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	protected override void Sculpt( Terrain terrain, TerrainPaintParameters paint, int res, int centerX, int centerY, int size )
	{
		var heightmap = terrain.Storage.HeightMap;
		float opacity = paint.BrushSettings.Opacity;
		int radius = size / 2;

		for ( int y = -radius; y <= radius; y++ )
		{
			for ( int x = -radius; x <= radius; x++ )
			{
				int tx = centerX + x;
				int ty = centerY + y;
				if ( tx < 0 || ty < 0 || tx >= res || ty >= res ) continue;

				float brush = SampleBrush( paint, x + radius, y + radius, size );
				if ( brush <= 0.001f ) continue;

				int index = ty * res + tx;
				float current = heightmap[index] / 65535f;

				// Depression with a raised rim: dip in the middle, bump near the edge
				float rim = brush < 0.75f ? -brush : (brush - 0.75f) / 0.25f;
				float target = current + rim * opacity;

				heightmap[index] = (ushort)Math.Clamp( target * 65535f, 0, 65535 );
			}
		}
	}
}

/// <summary>
/// Snaps heights to evenly spaced terraced steps within the brush footprint.
/// </summary>
[Title( "Terrace" )]
[Icon( "stairs" )]
[Alias( "tools.terrain.terrace" )]
[Group( "1" )]
[Order( 1 )]
public class TerraceBrushTool : CpuSculptBrushTool
{
	public TerraceBrushTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	protected override void Sculpt( Terrain terrain, TerrainPaintParameters paint, int res, int centerX, int centerY, int size )
	{
		var heightmap = terrain.Storage.HeightMap;
		float opacity = paint.BrushSettings.Opacity;
		int radius = size / 2;

		// Step every 8% of full height - you could expose this as a setting later
		const float stepSize = 0.08f;

		for ( int y = -radius; y <= radius; y++ )
		{
			for ( int x = -radius; x <= radius; x++ )
			{
				int tx = centerX + x;
				int ty = centerY + y;
				if ( tx < 0 || ty < 0 || tx >= res || ty >= res ) continue;

				float brush = SampleBrush( paint, x + radius, y + radius, size );
				if ( brush <= 0.001f ) continue;

				int index = ty * res + tx;
				float current = heightmap[index] / 65535f;

				float stepped = MathF.Round( current / stepSize ) * stepSize;
				float target = MathX.LerpTo( current, stepped, opacity * brush );

				heightmap[index] = (ushort)Math.Clamp( target * 65535f, 0, 65535 );
			}
		}
	}
}

/// <summary>
/// Adds adjustable simplex noise to the terrain, driven by the library's OpenSimplex2S
/// generator. Frequency, strength and seed can be tuned with the toolbar sliders.
/// </summary>
[Title( "Noise" )]
[Icon( "shuffle" )]
[Alias( "tools.terrain.noise" )]
[Group( "1" )]
[Order( 1 )]
public class NoiseBrushTool : CpuSculptBrushTool
{
	/// <summary>Noise frequency - higher = more, smaller bumps.</summary>
	[Property, Range( 0.5f, 20f ), Step( 0.1f ), WideMode] public float Frequency { get; set; } = 4f;

	/// <summary>How strongly the noise displaces the height (0..1, fraction of full height).</summary>
	[Property, Range( 0.001f, 0.05f ), Step( 0.001f ), WideMode] public float Strength { get; set; } = 0.008f;

	/// <summary>Random seed for the noise field.</summary>
	[Property, Range( 0, 100000 ), Step( 1 ), WideMode] public int NoiseSeed { get; set; } = 1337;

	public NoiseBrushTool( TerrainEditorTool terrainEditorTool ) : base( terrainEditorTool )
	{
	}

	// Noise is applied once across the whole dragged selection when the mouse is released,
	// not per-frame while painting.
	protected override bool ApplyOnRelease => true;

	/// <summary>
	/// Shows the stock terrain brush settings plus a "Noise Settings" group bound to the
	/// noise properties, so Frequency / Strength / Seed can be tuned in the sidebar.
	/// </summary>
	public override Widget CreateToolSidebar()
	{
		if ( _parent is null ) return null;

		var sidebar = (ToolSidebarWidget)_parent.CreateToolSidebar();
		if ( sidebar is null ) return null;

		var so = EditorTypeLibrary.GetSerializedObject( this );
		var group = sidebar.AddGroup( "Noise Settings" );

		var sheet = new ControlSheet();
		sheet.AddObject( so, prop =>
			prop.Name is nameof( Frequency ) or nameof( Strength ) or nameof( NoiseSeed ) );
		group.Add( sheet );

		return sidebar;
	}

	// Not used - ApplyOnRelease routes painting through ApplySelection instead.
	protected override void Sculpt( Terrain terrain, TerrainPaintParameters paint, int res, int centerX, int centerY, int size )
	{
	}

	/// <summary>
	/// Applies the noise field across every texel covered by the stroke, using the strongest
	/// brush falloff weight recorded for each texel.
	/// </summary>
	protected override void ApplySelection( Terrain terrain, int res, float[] weights, float opacity )
	{
		var heightmap = terrain.Storage.HeightMap;

		for ( int i = 0; i < weights.Length; i++ )
		{
			float w = weights[i];
			if ( w <= 0.001f ) continue;

			int tx = i % res;
			int ty = i / res;

			// Sample simplex noise at the texel, in [0,1], then remap to [-1,1]
			// so the noise can both add and subtract height.
			float noise = OpenSimplex2S.Noise2( NoiseSeed, tx * Frequency, ty * Frequency );
			noise = noise * 2f - 1f;

			float current = heightmap[i] / 65535f;
			float target = current + noise * Strength * opacity * w;

			heightmap[i] = (ushort)Math.Clamp( target * 65535f, 0, 65535 );
		}
	}

	public override Widget CreateToolbarWidget()
	{
		var group = new Widget();
		group.FixedHeight = Theme.RowHeight;
		group.Layout = Layout.Row();
		group.Layout.Spacing = 6;

		group.Layout.Add( new Label( "Frequency" ) );
		var freq = new FloatSlider( group );
		freq.Minimum = 0.5f;
		freq.Maximum = 20f;
		freq.Step = 0.1f;
		freq.Value = Frequency;
		freq.OnValueEdited = () => Frequency = freq.Value;
		group.Layout.Add( freq, 1 );

		group.Layout.Add( new Label( "Strength" ) );
		var strength = new FloatSlider( group );
		strength.Minimum = 0.001f;
		strength.Maximum = 0.05f;
		strength.Step = 0.001f;
		strength.Value = Strength;
		strength.OnValueEdited = () => Strength = strength.Value;
		group.Layout.Add( strength, 1 );

		group.Layout.Add( new Label( "Seed" ) );
		var seed = new FloatSlider( group );
		seed.Minimum = 0;
		seed.Maximum = 100000;
		seed.Step = 1;
		seed.Value = NoiseSeed;
		seed.OnValueEdited = () => NoiseSeed = (int)seed.Value;
		group.Layout.Add( seed, 1 );

		group.OnPaintOverride = () =>
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground );
			Paint.DrawRect( group.LocalRect, Theme.ControlRadius );
			return true;
		};

		return group;
	}
}
