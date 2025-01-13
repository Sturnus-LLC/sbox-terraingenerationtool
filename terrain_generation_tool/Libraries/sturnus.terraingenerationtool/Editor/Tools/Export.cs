using Editor;
using Editor.Widgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Tools
{

	class FilesystemTreeNode : TreeNode
	{
		public System.IO.FileSystemInfo Info;

		bool IsFolder => Info is System.IO.DirectoryInfo;

		public FilesystemTreeNode( string path )
		{
			if ( System.IO.Directory.Exists( path ) ) Info = new System.IO.DirectoryInfo( path );
			else if ( System.IO.File.Exists( path ) ) Info = new System.IO.FileInfo( path );
			else throw new Exception( "Invalid path" );
		}

		public override void OnPaint( VirtualWidget item )
		{
			PaintSelection( item );

			Paint.SetPen( IsFolder ? Theme.Yellow : Theme.White );
			Paint.DrawIcon( item.Rect, IsFolder ? "folder" : "description", 18, TextFlag.LeftCenter );

			Paint.SetPen( Theme.White );
			Paint.DrawText( item.Rect.Shrink( 24, 0, 0, 0 ), $"{Info.Name}", TextFlag.LeftCenter );
		}

		public int Order => Info is System.IO.DirectoryInfo ? 0 : 1;

		protected override void BuildChildren()
		{
			if ( Info is not System.IO.DirectoryInfo dirInfo )
				return;

			Clear();

			var infos = dirInfo.GetFileSystemInfos().Select( x => new FilesystemTreeNode( x.FullName ) );
			infos = infos.OrderBy( x => x.Order ).ThenBy( x => x.Info.Name );

			AddItems( infos );
		}
	}
	public class Export : Widget
	{
		public string filenamestring { get; set; }
		bool RawFile { get; set; } = true;
		bool SplatMapImage { get; set; } = true;
		bool HeightMapImage { get; set; } = true;
		bool TerrainGenerationValues { get; set; } = true;
		public Export( Widget parent ) : base( parent )
		{


			Layout = Layout.Column();
			var canvas = new Widget( null );
			canvas.Layout = Layout.Column();
			Layout.Margin = 10;
			//Layout.Spacing = 5;

			// Path Selection
			var exportpath = new Label.Subtitle( "Path" );
			Layout.Add( exportpath );
			var view = new TreeView( canvas );
			view.HorizontalSizeMode = SizeMode.CanGrow;
			var dir = view.AddItem( new FilesystemTreeNode( Editor.FileSystem.Content.GetFullPath( "" ) ) );
			view.Open( dir );
			canvas.Layout.Add( view, 1 );
			Layout.Add( canvas );

			// Filename input
			var filenamelabel = new Label.Subtitle( "Filename Prefix" );
			var filenamesublabel = new Label.Body( "(ie '/FilenamePrefix/_heightmap.raw')" );
			var filename = new StringControlWidget( this.GetSerialized().GetProperty( nameof( filenamestring ) ) );
			Layout.Add( filenamelabel );
			Layout.Add( filenamesublabel );
			Layout.Add( filename );

			// Export Options
			var exportoptionslabel = new Label.Subtitle( "Export Options" );
			Layout.Add( exportoptionslabel );
			var exportoptions = new Widget( null );
			exportoptions.Layout = Layout.Column();
			Layout.Add( exportoptions );
			var property = new ControlSheet();
			property.AddProperty( this, x => x.RawFile );
			property.AddProperty( this, x => x.HeightMapImage );
			property.AddProperty( this, x => x.SplatMapImage );
			property.AddProperty( this, x => x.TerrainGenerationValues);
			property.SetColumnStretch(0,1);
			property.SetMinimumColumnWidth( 500, 500 );
			Layout.Add(property);

			var exportlabel = new Label.Subtitle( "Export Files" );
			Layout.Add( exportlabel );
			var ExportButton = new Button("Export","file_download");
			ExportButton.Tint = "#41AF20";
			Layout.Add(ExportButton);

			ExportButton.Clicked += () =>
			{
				Log.Info( view.Selection.OnItemAdded.Target );
			};

			Layout.AddStretchCell();


		}

		[TerrainGenerationTool]
		[Title( "Export" )]
		[Icon( "file_download" )]
		[Order( 5 )]
		internal static Widget ExportPage()
		{
			var canvas = new Export( null );

			return canvas;
		}
	}

}
