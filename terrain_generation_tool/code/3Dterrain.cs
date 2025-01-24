using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

namespace Sturnus.TerrainGenerationTool.ThreeDimensionalTerrain;
	public class TerrainTransvoxel : Component
	{
		private VoxelStorage _storage;
		private SceneObject _sceneObject;

		public struct Vertex
		{
			public Vector3 Position;

			public Vertex( Vector3 position )
			{
				Position = position;
			}
		}

		[Property]
		public VoxelStorage Storage
		{
			get => _storage;
			set
			{
				if ( _storage != value )
				{
					_storage = value;
					RebuildTerrain();
				}
			}
		}

		[Property]
		public Material TerrainMaterial { get; set; }

		protected override void OnEnabled()
		{
			base.OnEnabled();
			if ( _storage != null )
			{
				RebuildTerrain();
			}
		}

		protected override void OnDisabled()
		{
			base.OnDisabled();
			_sceneObject?.Delete();
			_sceneObject = null;
		}

		public void RebuildTerrain()
		{
			if ( _storage == null )
			{
				Log.Warning( "Voxel storage is not assigned." );
				return;
			}

			var mesh = GenerateTransvoxelMesh( _storage );
			if ( mesh != null )
			{
				var model = Model.Builder.AddMesh( mesh ).Create();
				_sceneObject?.Delete();

				// Convert GameTransform to Transform
				Transform transform = new Transform
				{
					Position = WorldPosition,
					Rotation = WorldRotation,
					Scale = WorldScale
				};

				_sceneObject = new SceneObject( Scene.SceneWorld, model, transform );
				_sceneObject.SetMaterialOverride( TerrainMaterial );
			}
		}

		public Mesh GenerateTransvoxelMesh( VoxelStorage storage )
		{
			var mesh = new Mesh();
			var vertices = new List<Vector3>();
			var indices = new List<int>();

			foreach ( var cell in storage.GetTransvoxelCells() )
			{
				var generated = Transvoxel.GenerateMesh( cell );
				vertices.AddRange( generated.Vertices );
				indices.AddRange( generated.Indices );
			}

			// Define VertexAttribute array for positions
			VertexAttribute[] vertexAttributes = vertices
				.Select( v => new VertexAttribute( VertexAttributeType.Position, VertexAttributeFormat.Float32 ) )
				.ToArray();

			// Explicitly specify VertexAttribute type for the vertex buffer
			mesh.CreateVertexBuffer<VertexAttribute>( vertexAttributes );

			// Populate the index buffer
			mesh.SetIndexBufferData( indices.ToArray() );

			return mesh;
		}

	public void PopulateVoxelStorage( VoxelStorage storage, int resolution, float voxelSize, float heightScale )
	{
		storage.Resolution = resolution;
		storage.VoxelSize = voxelSize;
		storage.Data = new byte[resolution, resolution, resolution];

		var perlin = new PerlinNoise(); // Simple Perlin noise generator

		for ( int x = 0; x < resolution; x++ )
		{
			for ( int y = 0; y < resolution; y++ )
			{
				for ( int z = 0; z < resolution; z++ )
				{
					// Generate height based on Perlin noise
					float nx = (float)x / resolution;
					float ny = (float)y / resolution;
					float nz = (float)z / resolution;

					float noiseValue = perlin.Noise( nx * 10f, ny * 10f, nz * 10f );

					// Scale the noise value to the voxel height
					float height = noiseValue * heightScale;

					// Populate voxel data based on height
					storage.Data[x, y, z] = (byte)((z < height) ? 255 : 0); // 255 for solid, 0 for empty
				}
			}
		}
	}

	public class PerlinNoise
	{
		public float Noise( float x, float y, float z )
		{
			// Implement Perlin noise or use a library
			// This is a placeholder for demonstration
			return (float)(Math.Sin( x * 2.0f * Math.PI ) + Math.Sin( y * 2.0f * Math.PI ) + Math.Sin( z * 2.0f * Math.PI )) / 3.0f;
		}
	}

	
}

public class VoxelStorage
	{
		public int Resolution { get; set; }
		public float VoxelSize { get; set; }
		public byte[,,] Data { get; set; }

		public IEnumerable<VoxelCell> GetTransvoxelCells()
		{
			for ( int x = 0; x < Resolution - 1; x++ )
			{
				for ( int y = 0; y < Resolution - 1; y++ )
				{
					for ( int z = 0; z < Resolution - 1; z++ )
					{
						yield return new VoxelCell
						{
							Voxels = new byte[]
							{
								Data[x, y, z], Data[x + 1, y, z],
								Data[x, y + 1, z], Data[x + 1, y + 1, z],
								Data[x, y, z + 1], Data[x + 1, y, z + 1],
								Data[x, y + 1, z + 1], Data[x + 1, y + 1, z + 1]
							},
							Position = new Vector3( x, y, z ) * VoxelSize
						};
					}
				}
			}
		}
	}

	public struct VoxelCell
	{
		public byte[] Voxels;
		public Vector3 Position;
	}

	public static class Transvoxel
	{
		public static (List<Vector3> Vertices, List<int> Indices) GenerateMesh( VoxelCell cell )
		{
			var vertices = new List<Vector3>();
			var indices = new List<int>();

			for ( int i = 0; i < cell.Voxels.Length; i++ )
			{
				if ( cell.Voxels[i] > 127 )
				{
					vertices.Add( cell.Position + Vector3.One * (i / 8f) );
					indices.Add( vertices.Count - 1 );
				}
			}

			return (vertices, indices);
		}
	}


