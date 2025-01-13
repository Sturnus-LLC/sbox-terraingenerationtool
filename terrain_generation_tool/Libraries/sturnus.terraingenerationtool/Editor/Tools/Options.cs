using Editor;
using Editor.Widgets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Tools
{
	public class TerrainGenerationToolOptions : Widget
	{
		public TerrainGenerationToolOptions( Widget parent ) : base( parent )
		{
			Layout = Layout.Column();
			var Test = new Button( "Normal Button", "people" );
			Layout.Add( Test );
		}

		/*[TerrainGenerationTool]
		[Title( "Options" )]
		[Icon( "settings" )]
		[Order( 4 )]*/
		internal static Widget TerrainGenerationTool()
		{
			var canvas = new TerrainGenerationToolOptions( null );

			return new Label.Subtitle( "No options yet..." ); ;
		}
	}

}
