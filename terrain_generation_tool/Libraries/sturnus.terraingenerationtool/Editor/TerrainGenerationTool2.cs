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

[System.AttributeUsage( System.AttributeTargets.Method )]
public class TerrainGenerationToolAttribute : System.Attribute
{

}

[EditorApp( "Terrain Generation Tool", "terrain", "Generate HeightMap and Splatmaps." )]
public class MyEditorApp : Window
{
	NavigationView View;
	public MyEditorApp()
	{
		WindowTitle = "Terrain Generation Tool";
		SetWindowIcon( "terrain" );
		Size = new Vector2( 1280, 800 );
		View = new NavigationView( this );
		View.Size = Size;

		Layout = Layout.Row();
		Layout.Add( View, 1 );

		Rebuild();
		Show();
	}

	[EditorEvent.Hotload]
	public void Rebuild()
	{
		View.ClearPages();

		var methods = EditorTypeLibrary.GetMethodsWithAttribute<TerrainGenerationToolAttribute>().Select( x => x.Method );
		foreach ( var g in methods.GroupBy( x => x.Group ?? x.Title ).OrderBy( x => x.Key ) )
		{
			var f = g.First();

			var option = new NavigationView.Option( g.Key, f.Icon );
			option.CreatePage = () =>
			{
				var scroll = new ScrollArea( null );
				scroll.Canvas = new Widget( scroll );
				scroll.Canvas.Layout = Layout.Column();
				scroll.Canvas.Layout.Margin = 32;

				var body = scroll.Canvas.Layout;

				foreach ( var m in g )
				{
					var widget = m.InvokeWithReturn<Widget>( null );

					body.Add( new Label.Subtitle( m.Title ) );

					if ( m.Description != null )
						body.Add( new Label.Body( m.Description ) );

					body.Add( widget, 1 );
					body.AddSpacingCell( 32 );
				}

				body.AddStretchCell();

				return scroll;
			};

			View.AddPage( option );
		}
	}

	public class ColouredLabel : Widget
	{
		Color color;
		string label;

		public ColouredLabel( Color color, string label ) : base( null )
		{
			this.color = color;
			this.label = label;
			MinimumSize = 100;
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( color.Darken( 0.4f ) );
			Paint.DrawRect( LocalRect );

			Paint.SetPen( color );
			Paint.DrawText( LocalRect, label, TextFlag.Center | TextFlag.WordWrap );
		}
	}
}
