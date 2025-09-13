using System;
using Godot;
using Godot.Collections;
using System.Collections.Generic;
using static MemoryHelpers;
using System.Runtime.InteropServices;


[Tool]
[GlobalClass]
public partial class Shapes: CompositorEffect {
	public required RenderingDevice renderingDevice;
	public required ShapeResources[] shapeResourcesArray;
	public required Rid?[] shapePipelines = new Rid?[NUM_SHAPE_TYPES];

	Rid _cachedImgTexture;
	Rid _cachedDepthTexture;
	Rid _framebuffer;

	PolylineBuffers polylineBuffers;


	// fatty(?) struct that we'll use to hold all of the gpu resource RIDs for a particular shape type
	public struct ShapeResources {
		ShapeType shapeType;

		public long vtxFormat;
		public Rid vtxPosBuffer;
		public Rid vtxTexcoordBuffer;
		public Rid vtxArray;

		public Rid idxBuffer;
		public Rid idxArray;
		public Rid shader;

		public Rid instDataBuffer;

		public void Free(RenderingDevice rd) {
			Span<Rid> rids = stackalloc Rid[] {
				vtxPosBuffer,
				vtxTexcoordBuffer,
				vtxArray,
				idxBuffer,
				idxArray,
				shader,
				instDataBuffer,

    		};
    		foreach (var rid in rids) {
    			if (rid.IsValid) rd.FreeRid(rid);
    		}
		}

	}

	const int DEBUG_INSTANCES = 100;

	// Godot seems to require a constructor
	public Shapes():base() {
		this.renderingDevice = RenderingServer.GetRenderingDevice();
		this.shapeResourcesArray = new ShapeResources[NUM_SHAPE_TYPES];
		this.shapePipelines = new Rid?[NUM_SHAPE_TYPES];
		_Init(this);
	}

	public static readonly int NUM_SHAPE_TYPES = Enum.GetValues<ShapeType>().Length;
	public enum ShapeType {
		Polyline = 0, 
		// Circle = 1,
	}

	static Rid MakePipeline(RenderingDevice rd, long vertexFormat, Rid shader, Rid framebuffer) {
		var framebufferFmt = rd.FramebufferGetFormat(framebuffer);
		return rd.RenderPipelineCreate(
			shader, 
			framebufferFmt,
			vertexFormat,
			RenderingDevice.RenderPrimitive.Triangles,
			new RDPipelineRasterizationState() {
				CullMode = RenderingDevice.PolygonCullMode.Disabled,
			},
			new RDPipelineMultisampleState() {
				EnableSampleShading = false,
				SampleCount = RenderingDevice.TextureSamples.Samples1,
				MinSampleShading = 1f,
			},
			new RDPipelineDepthStencilState() {
				EnableDepthTest = true,
				DepthCompareOperator = RenderingDevice.CompareOperator.GreaterOrEqual,
				EnableDepthWrite = true,
			},
			new RDPipelineColorBlendState() {
				Attachments = new Array<RDPipelineColorBlendStateAttachment> () {
					new(), 
				},
			}
		);
	}

    public static void _Init(Shapes instance) {
		ShapeResources InitPolylineResources() {
	    	ReadOnlySpan<Vector3> verts = stackalloc Vector3[] {
	            new(-0.5f, -0.5f, 0f),
	            new(-0.5f,  0.5f, 0f),
	            new(0.5f,  0.5f, 0f),
	            new(0.5f,  -0.5f, 0f)
	    	};
	    	ReadOnlySpan<Vector2> texcoords = stackalloc Vector2[] {
	    		new(0f, 0f),
	    		new(0f, 1f),
	    		new(1f, 1f),
	    		new(1f, 0f),
	    	};
	    	ReadOnlySpan<uint> ids = stackalloc uint[] { 
	    		0, 2, 1,
	    		0, 3, 2,
			};
			var vtxPosBuffer = instance.renderingDevice.VertexBufferCreate(
				(uint)(SizeOf<Vector3>() * verts.Length), 
				verts.AsBytes(),
				0
			 );
			var vtxTexcoordBuffer = instance.renderingDevice.VertexBufferCreate(
				(uint)(SizeOf<Vector2>() * texcoords.Length),
				texcoords.AsBytes(),
				0
			);
			var vtxFormat = instance.renderingDevice.VertexFormatCreate(
			new Array<RDVertexAttribute>() {
					new() {
						Format = RenderingDevice.DataFormat.R32G32B32Sfloat,
						Frequency = RenderingDevice.VertexFrequency.Vertex,
						Location = 0,
						Offset = 0,
						Stride = (uint)(SizeOf<Vector3>()),
					},
					new() {
						Format = RenderingDevice.DataFormat.R32G32Sfloat,
						Frequency = RenderingDevice.VertexFrequency.Vertex,
						Location = 1,
						Offset = 0,
						Stride = (uint)SizeOf<Vector2>(), 
					},
				}
			);
			var vtxArray = instance.renderingDevice.VertexArrayCreate(
				(uint)verts.Length, 
				vtxFormat, 
				new Array<Rid> { vtxPosBuffer, vtxTexcoordBuffer }
			);
			var idxBuffer = instance.renderingDevice.IndexBufferCreate(
				(uint)ids.Length, 
				RenderingDevice.IndexBufferFormat.Uint32, 
				ids.AsBytes(), 
				false, 
				0
			);
			var idxArray = instance.renderingDevice.IndexArrayCreate(
				idxBuffer, 0, (uint)ids.Length
			);

			var shaderFile = GD.Load<RDShaderFile>("res://addons/ShapesRenderer/Shaders/polyline.glsl");
			var spv = shaderFile.GetSpirV();
			var shader = instance.renderingDevice.ShaderCreateFromSpirV(spv);

			return new() {
				vtxPosBuffer = vtxPosBuffer,
				vtxTexcoordBuffer = vtxTexcoordBuffer,
				vtxArray = vtxArray,
				vtxFormat = vtxFormat,

				idxBuffer = idxBuffer,
				idxArray = idxArray,

				shader = shader,
			};
		}

		instance.polylineBuffers = new() {
			pointsBuffer =  new() {
				rid = instance.renderingDevice.StorageBufferCreate(8192),
				sizeBytes = 8192,	
			},
			idsBuffer = new() {
				rid = instance.renderingDevice.StorageBufferCreate(8192),
				sizeBytes = 8192,
			},
			colorsBuffer = new() {
				rid = instance.renderingDevice.StorageBufferCreate(8192),
				sizeBytes = 8192,
			},
			widthsBuffer = new() {
				rid = instance.renderingDevice.StorageBufferCreate(8192),
				sizeBytes = 8192,
			},
			lineInfoBuffer = new() {
				rid = instance.renderingDevice.StorageBufferCreate(8192),
				sizeBytes = 8192,
			}
		};

		// ShapeResources InitCircleResources() {
		// 	return default; // stub
		// }

		for (int i = 0; i < NUM_SHAPE_TYPES; i++) {
			var shapeType = (ShapeType)(i);
			instance.shapeResourcesArray[i] = shapeType switch {
				ShapeType.Polyline => InitPolylineResources(),
				// ShapeType.Circle => InitCircleResources(),
				_ => default,
			};
		}
    }

    public override void _Notification(int what) {
    	if (what == NotificationPredelete) {
    		foreach (var resources in shapeResourcesArray) {
    			resources.Free(renderingDevice);
    		}
    	}
    }

    static readonly List<Vector3> sph = FibonacciSphere(10, 0.2f);

    static List<Vector3> FibonacciSphere(int points, float radius) {
    	List<Vector3> pts = new(points);
    	const float TOTAL_ROTATIONS = 0.1f;
    	for (int i = 0; i < points; i++) {
    		var t0 = i / (points - 1f);
    		var theta = Mathf.Pi * (t0 - 0.5f);
    		var phi = TOTAL_ROTATIONS * Mathf.Pi * 2f * t0;
    		float x = (float)(Mathf.Cos(theta));
    		float y = (float)(Math.Sin(theta));
    		float z = 0;
    		pts.Add(new(x,y,z));
    	}
    	return pts;
    }


    void DrawDemo1() {

    	Span<Vector3> pts = stackalloc Vector3[700];
    	const float TOTAL_ROTATIONS = 20f;

    	{
    		var builder = Polyline.Begin();
			for (int i = 0; i < pts.Length; i++) {
	    		var t0 = i / (pts.Length - 1f);
	    		var theta = Mathf.Pi * (t0 - 0.5f);
	    		var phi = TOTAL_ROTATIONS * Mathf.Pi * 2.0 * t0;
	    		float x = (float)(Mathf.Cos(theta) * Math.Cos(phi));
	    		float y = (float)(Math.Sin(theta));
	    		float z = (float)(Math.Cos(theta) * Math.Sin(phi));
	    		builder.Point(new(x,y,z));
	    	}
	    	builder.Color(Colors.Red).Color(Colors.Blue).Width(10f).End();
    	}

    	{
    		var builder = Polyline.Begin();
			for (int i = 0; i < pts.Length; i++) {
	    		var t0 = i / (pts.Length - 1f);
	    		var theta = Mathf.Pi * (t0 - 0.5f);
	    		var phi = TOTAL_ROTATIONS * Mathf.Pi * 2.0 * t0;
	    		float x = (float)(Mathf.Cos(theta) * Math.Cos(phi));
	    		float y = (float)(Math.Sin(theta));
	    		float z = (float)(Math.Cos(theta) * Math.Sin(phi));
	    		builder.Point(new Vector3(x,y,z) + Vector3.Right * 2f);
	    	}
	    	builder.Color(Colors.Blue).Color(Colors.GreenYellow).Width(10f).End();

    	}

    }

    
    float _demot = 0f;
    void DrawDemo2() {
    	const int GRID_X = 100;
    	const int GRID_Y = 40;

    	
    	var noise = new Godot.FastNoiseLite();

    	var colorA = new Color(Colors.SlateBlue);
    	var colorB = new Color(Colors.IndianRed);
    	var nColor = new Color(Colors.LightYellow);
    	var t = Engine.GetProcessFrames() * 0.1f;

    	const float NOISE_SCALE = 30f;
    	for (int y = 0; y < GRID_Y; y++) {

			float py = y / (GRID_Y - 1f);
    		var baseColor = colorA + (colorB - colorA)*py;
    		
    		var builder = Polyline.Begin().Width(5f);
    		for (int x = 0; x < GRID_X; x++) {
    			float px = x / (GRID_X - 1f);
    			float n = noise.GetNoise3D(px * NOISE_SCALE, py * NOISE_SCALE + 100f, _demot * 10f);


    			n = n * 0.5f + 0.5f;
    			float nh = n * n * n;
    			float nb = Godot.Mathf.SmoothStep(0.25f, 0.75f, nh);
    			builder.Point(new(px * 0.5f, nh * 0.5f, py * 0.5f));

    			var c = baseColor + (nColor - baseColor)*nb;
    			c = c.SrgbToLinear();
    			builder.Color(c);
    		}
    		builder.End();
    	}
    	_demot += 0.016f;
    }

    public override void _RenderCallback(int callbackType, RenderData renderData) {

    	if (renderingDevice is {} rd) {
    		if (callbackType == (int)EffectCallbackTypeEnum.PostTransparent) {

    			DrawDemo1();
    			if (renderData.GetRenderSceneBuffers() is RenderSceneBuffersRD sb) {
    				var size = sb.GetInternalSize();

    				// render helper fns
			    	void RenderPolylines(ref ShapeResources resources, Rid framebuffer) {
			    		int shapeIndex = (int)ShapeType.Polyline;
			    		shapePipelines[shapeIndex] ??= MakePipeline(
			    			renderingDevice, 
			    			resources.vtxFormat, 
			    			resources.shader, 
			    			framebuffer
						);
						
						var pipeline = shapePipelines[shapeIndex]!.Value;
						var rd = renderingDevice;

			    		if (Polyline._FinalizeBatch(ref polylineBuffers, resources.shader, rd)) {
			    			if (!polylineBuffers.shapeUniforms.IsValid) {
			    				RDUniform MakeUniform(Rid buffer, int binding) {
			    					RDUniform uniform = new() {
			    						Binding = binding,
			    						UniformType = RenderingDevice.UniformType.StorageBuffer,
			    					};
			    					uniform.AddId(buffer);
			    					return uniform;
			    				}
			    				polylineBuffers.shapeUniforms = renderingDevice.UniformSetCreate(
			    					new Array<RDUniform>() {
			    						MakeUniform(polylineBuffers.pointsBuffer.rid, 0),
			    						MakeUniform(polylineBuffers.idsBuffer.rid, 1),
			    						MakeUniform(polylineBuffers.colorsBuffer.rid, 2),
			    						MakeUniform(polylineBuffers.widthsBuffer.rid, 3),
			    						MakeUniform(polylineBuffers.lineInfoBuffer.rid, 4),
			    					},
			    					resources.shader, 0
								);
							}

			    			uint numSegments = (uint)polylineBuffers.numSegments;
					    	var drawList = rd.DrawListBegin(framebuffer);

							Span<Polyline.PushConstants> pcBuf = stackalloc Polyline.PushConstants[1];

							var proj = renderData.GetRenderSceneData().GetViewProjection(0);
							var view = new Projection(renderData.GetRenderSceneData().GetCamTransform().Inverse());
							pcBuf[0] = new() {
								viewProj = proj * view,
								screenParams = new () {
									X = size.X,
									Y = size.Y,
									Z = 1f / size.X,
									W = 1f / size.Y,
								}
							};

							// GD.Print($"vp: {pcBuf[0].viewProj.X}");
					    	ReadOnlySpan<byte> pcBytes = pcBuf.AsBytes();
					    	rd.DrawListBindRenderPipeline(drawList, pipeline);
					    	rd.DrawListBindVertexArray(drawList, resources.vtxArray);
					    	rd.DrawListBindIndexArray(drawList, resources.idxArray);
					    	renderingDevice.DrawListSetPushConstant(drawList, pcBytes, (uint)pcBytes.Length);
						   	rd.DrawListBindUniformSet(drawList, polylineBuffers.shapeUniforms, 0);

						   	// GD.Print($"num segments: {numSegments}");
					    	rd.DrawListDraw(drawList, true, numSegments);
					    	rd.DrawListEnd();
							rd.DrawCommandEndLabel();
			    		}

			    		Polyline.Reset();
			    	}

    				if (size.X > 0 && size.Y > 0) {
    					// need framebuffer to create the pipeline, so we do both lazily.
    					// refresh FBO if stale
    					var imgTexture = sb.GetColorTexture();
    					var depthTexture = sb.GetDepthTexture();
    					if (imgTexture != _cachedImgTexture || depthTexture != _cachedDepthTexture) {
    						if (_framebuffer.IsValid) {
								if (rd.FramebufferIsValid(_framebuffer)) rd.FreeRid(_framebuffer);
    						}

    						// cache the color and depth targets, and invalidate the framebuffer and pipelines
    						_cachedImgTexture = imgTexture;
    						_cachedDepthTexture = depthTexture;
    						
    						for (int i = 0; i < shapePipelines.Length; i++) {
    							if (shapePipelines[i] is {} pipeline && pipeline.IsValid) {
    								renderingDevice.FreeRid(pipeline);	
    							}
    							shapePipelines[i] = null;
    						}
    					}
    					_framebuffer = rd.FramebufferCreate(new Array<Rid>{ _cachedImgTexture, _cachedDepthTexture });
    				}

    				if (_framebuffer is {} framebuffer) {
						for (int i = 0; i < NUM_SHAPE_TYPES; i++) {
							ref var resources = ref shapeResourcesArray[i];
							shapePipelines[i] ??= MakePipeline(renderingDevice, resources.vtxFormat, resources.shader, framebuffer);
							ShapeType shapeType = (ShapeType)i;
							switch (shapeType) {
								case ShapeType.Polyline:
									RenderPolylines(ref resources, framebuffer);
									break;
							}
						}
					}
    			}
    		}
    	}
    }
}

	public struct UniformBuffer {
		public Rid rid;
		public uint sizeBytes;
	}

	public struct PolylineBuffers {
		public Rid shapeUniforms;
		public int numSegments;

		public UniformBuffer pointsBuffer;
		public UniformBuffer colorsBuffer;
		public UniformBuffer widthsBuffer;
		public UniformBuffer lineInfoBuffer;
		public UniformBuffer idsBuffer;

		public void Free(RenderingDevice rd) {
			Span<Rid> rids = stackalloc Rid[] {
				pointsBuffer.rid, colorsBuffer.rid, widthsBuffer.rid, lineInfoBuffer.rid, idsBuffer.rid,
			};
			foreach (var rid in rids) {
				if (rid.IsValid) rd.FreeRid(rid);
			}
		}
	}

public static class Polyline {
    	static LinearArena _Arena = LinearArena.Create(8192);
    	static PolylineBatch _ActiveBatch;
    	public static Builder Begin() {
    		if (!_ActiveBatch.isCreated) {
    			_ActiveBatch = PolylineBatch.Create(_Arena);
    		}
    		var builder = new Builder() {
    			arena = _Arena,
    		};
    		return builder;
    	}

    	static void FinalizeLine(ref Builder b) {
    		if (_ActiveBatch.isCreated) {
    			_ActiveBatch.DescriptorSubmit(ref b);	
    		}	
    	} 

    	public static void Reset() {
    		if (_Arena.Offset > 0) {
    			_Arena.Reset();
    		}
    		_ActiveBatch = default;
    	}

    	[StructLayout(LayoutKind.Sequential)]
    	public struct PushConstants {
    		public Projection viewProj;
    		public Vector4 screenParams;
    	}

    	public struct Builder {
			public LinearArena arena;
			public LinearArena.List<Vector3> pointsList;
			public LinearArena.List<Color> colorsList;
			public LinearArena.List<float> widthsList;

			const int MIN_BUF_SIZE = 4;

			public Builder Point(Vector3 point) {
				if (!pointsList.IsCreated()) {
					pointsList = arena.AllocList<Vector3>(MIN_BUF_SIZE);	
				}
				pointsList.Append(point);
				return this;
			}
			public Builder Points(ReadOnlySpan<Vector3> points) {
				if (!pointsList.IsCreated()) {
					pointsList = arena.AllocList<Vector3>(MIN_BUF_SIZE);	
				}
				pointsList.AppendRange(points);
				return this;
			}
			public Builder Points(Vector3[] points) => Points(points.AsReadOnlySpan());
			public Builder Points(List<Vector3> points) => Points(CollectionsMarshal.AsSpan(points));

			public Builder Color(Color color) {
				if (!colorsList.IsCreated()) {
					colorsList = arena.AllocList<Color>(MIN_BUF_SIZE);
				}
				colorsList.Append(color);
				return this;
			}
			public Builder Colors(ReadOnlySpan<Color> colors) {
				if (!colorsList.IsCreated()) {
					colorsList = arena.AllocList<Color>(MIN_BUF_SIZE);
				}
				colorsList.AppendRange(colors);
				return this;
			}
			public Builder Colors(Color[] colors) => Colors(colors.AsReadOnlySpan());
			public Builder Colors(List<Color> colors) => Colors(CollectionsMarshal.AsSpan(colors));


			public Builder Width(float width) {
				if (!widthsList.IsCreated()) {
					widthsList = arena.AllocList<float>(MIN_BUF_SIZE);
				}
				widthsList.Append(width);
				return this;
			}
			public Builder Widths(ReadOnlySpan<float> widths) {
				if (!widthsList.IsCreated()) {
					widthsList = arena.AllocList<float>(MIN_BUF_SIZE);
				}
				widthsList.AppendRange(widths);
				return this;
			}
			public Builder Widths(float[] widths) => Widths(widths.AsReadOnlySpan());
			public Builder Widths(List<float> widths) => Widths(CollectionsMarshal.AsSpan(widths));

			public void End() {
				FinalizeLine(ref this);
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct PolylineBatch {

			public required LinearArena arena;
			
			public required LinearArena.List<LinearArena.List<Vector3>> batchPoints;
			public required LinearArena.List<LinearArena.Buffer<uint>> batchLineIDs;
			public required LinearArena.List<LinearArena.List<Color>> batchColors;
			public required LinearArena.List<LinearArena.List<float>> batchWidths;
			public required LinearArena.List<LineInfo> batchLines;

			public int totalPoints;
			public int totalColors;
			public int totalWidths;
			public bool isCreated;

			[StructLayout(LayoutKind.Sequential, Size = 32)]
			public struct LineInfo {
				public int pointsStart;
				public int pointsLen;
				public int colorsStart;
				public int colorsLen;
				public int widthsStart;
				public int widthsLen;
			}

			public static PolylineBatch Create(LinearArena arena) {
				return new() {
					batchLines = arena.AllocList<LineInfo>(128),
				 	batchPoints = arena.AllocList<LinearArena.List<Vector3>>(128),
				 	batchLineIDs = arena.AllocList<LinearArena.Buffer<uint>>(128),
				 	batchColors = arena.AllocList<LinearArena.List<Color>>(128),
				 	batchWidths = arena.AllocList<LinearArena.List<float>>(128),
					arena = arena,
					isCreated = true,
				};
			}

			public void DescriptorSubmit(ref Polyline.Builder b) {
				int numPoints = b.pointsList.Length;
				if (numPoints < 2) {
					return;
				}
				if (!b.widthsList.IsCreated()) {
					b.Width(1f);
				}
				if (!b.colorsList.IsCreated()) {
					b.Color(Colors.Magenta);
				}

				// create line IDs buffer
				uint lineID = (uint)batchLines.Length;
				var lineIDs = arena.AllocBuffer<uint>(numPoints);
				lineIDs.Items().Fill(lineID);
				batchLineIDs.Append(lineIDs);

				batchPoints.Append(b.pointsList);
				batchColors.Append(b.colorsList);
				batchWidths.Append(b.widthsList);

				batchLines.Append(new() {
					pointsStart = totalPoints,
					pointsLen = numPoints,

					colorsStart = totalColors,
					colorsLen = b.colorsList.Length,

					widthsStart = totalWidths,
					widthsLen =  b.widthsList.Length,
				});

				// Godot.GD.Print($"line: {batchLines.Items()[0].pointsStart}, {batchLines.Items()[0].pointsLen}");

				totalPoints += b.pointsList.Length;
				totalColors += b.colorsList.Length;
				totalWidths += b.widthsList.Length;
			}
		}

    	public static bool _FinalizeBatch(ref PolylineBuffers polylineBuffers, Rid shader, RenderingDevice renderingDevice) {
    		bool result = false;

    		if (_ActiveBatch.isCreated && _ActiveBatch.batchLines.Length > 0) {
    			result = true;
    			polylineBuffers.numSegments = _ActiveBatch.totalPoints - 1;

    			bool reallocatedAny = false;
    			void ResizeBufferIfNeeded<T>(ref UniformBuffer ub, int numElements) where T: unmanaged {
    				var sizeBytes = (uint)(numElements * SizeOf<T>());
    				if (sizeBytes > ub.sizeBytes) {
    					renderingDevice.FreeRid(ub.rid);
    					ub.rid = renderingDevice.StorageBufferCreate(sizeBytes);
    					ub.sizeBytes = sizeBytes;
    					GD.Print($"resized to {sizeBytes}");
    				}
    			}

				// invalidate uniforms
    			if (reallocatedAny) {
    				polylineBuffers.shapeUniforms = default;
    			}

				// var pts = _ActiveBatch.batchPoints.Items()[0];
				// for (int i = 0; i < _ActiveBatch.totalPoints; i++) {
				// 	GD.Print(pts.Items()[i]);
				// }

    			ResizeBufferIfNeeded<Vector3>(ref polylineBuffers.pointsBuffer,_ActiveBatch.totalPoints);
    			ResizeBufferIfNeeded<uint>(ref polylineBuffers.idsBuffer,_ActiveBatch.totalPoints);
    			ResizeBufferIfNeeded<Color>(ref polylineBuffers.colorsBuffer,_ActiveBatch.totalColors);
    			ResizeBufferIfNeeded<float>(ref polylineBuffers.widthsBuffer,_ActiveBatch.totalWidths);
    			ResizeBufferIfNeeded<PolylineBatch.LineInfo>(ref polylineBuffers.lineInfoBuffer, _ActiveBatch.batchLines.Length);

    			void UploadData<T>(UniformBuffer ub, LinearArena.List<LinearArena.List<T>> batchData) where T: unmanaged {
    				uint offset = 0;
	    			foreach (var list in batchData.Items()) {
	    				ReadOnlySpan<T> span = list.Items();
	    				var bytes = span.AsBytes();
	    				renderingDevice.BufferUpdate(ub.rid, offset, (uint)bytes.Length, bytes);
	    				offset += (uint)bytes.Length;
	    			}
    			}

    			// TODO at some point examine where it's faster to do this interleaved instead of sequentially
    			UploadData(polylineBuffers.pointsBuffer,_ActiveBatch.batchPoints);
    			UploadData(polylineBuffers.colorsBuffer,_ActiveBatch.batchColors);
    			UploadData(polylineBuffers.widthsBuffer,_ActiveBatch.batchWidths);

    			// ugly but the ids buffer uses a different type (Buffer<T> instead of List<T>)
    			// so we do this manually
    			{
    				uint offset = 0;
    				foreach (var buf in _ActiveBatch.batchLineIDs.Items()) {
    					ReadOnlySpan<uint> span = buf.Items();
	    				var bytes = span.AsBytes();
	    				renderingDevice.BufferUpdate(polylineBuffers.idsBuffer.rid, offset, (uint)bytes.Length, bytes);
	    				offset += (uint)bytes.Length;
    				}
    			}

    			// and the lines are just a single flat list
    			var lineInfoBytes = _ActiveBatch.batchLines.Items().AsBytes();
    			renderingDevice.BufferUpdate(polylineBuffers.lineInfoBuffer.rid, 0, (uint)lineInfoBytes.Length, lineInfoBytes);
    		}

    		Polyline.Reset();
    		return result;
    	}
    }