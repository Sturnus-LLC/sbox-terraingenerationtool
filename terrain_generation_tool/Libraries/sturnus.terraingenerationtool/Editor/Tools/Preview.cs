using Editor;
using Editor.Widgets;
using Sandbox.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Sandbox.VertexLayout;
using Sturnus.TerrainGenerationTool.Generation;
using Sandbox;
using System.Security.Cryptography.X509Certificates;

public class TerrainGenerationToolPreview : Widget
	{
		float[,] _heightmap;

		private readonly SceneRenderingWidget RenderCanvas;
		private readonly CameraComponent Camera;
		private readonly Terrain terrain;
		private readonly Gizmo.Instance GizmoInstance;

		public void HeightMapUpdate( ushort[] heightmap)
		{
			terrain.Storage.HeightMap = heightmap;
		}

		public TerrainGenerationToolPreview( Widget parent ) : base( parent )
		{

			Layout = Layout.Row();
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
				Camera.LocalRotation = new Angles(45,0,0);

				RenderCanvas.Camera = Camera;

				terrain = new GameObject( true, "terrain" ).GetOrAddComponent<Terrain>( true );

				//terrain.Storage.HeightMap = ;
			}

			GizmoInstance = RenderCanvas.GizmoInstance;
			var column = Layout.AddColumn( 1 );

			column.Add( RenderCanvas, 1 );

			Layout.AddStretchCell();
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
						var p = new Vector3( x, y, 0f);

						if ( i > 0 )
						{
							Gizmo.Draw.Line( last, p );
						}

						last = p;
					}
				}
			}
		}


		[TerrainGenerationTool]
		[Title( "Preview" )]
		[Icon( "preview" )]
		[Order( 2 )]
		internal static Widget TerrainGenerationToolz()
		{
			var canvas = new TerrainGenerationToolPreview( null );

			return canvas;
		}
	}
