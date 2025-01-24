using Sandbox;
using System.Threading.Tasks;

using Sturnus.TerrainGenerationTool.ThreeDimensionalTerrain;


public sealed class Test : Component
{

	[Property] public List<string> Testing { get; set; } = new List<string> { "one","two","three" };
	//[Property] public List<string> Names { get; set; } = ["Terry", "Garry"];

	protected override void OnUpdate()
	{

	}

	protected override async Task OnLoad()
	{
		Testing.Add( "four" );
	}
}

