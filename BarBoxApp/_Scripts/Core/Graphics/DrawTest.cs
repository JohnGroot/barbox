using System;
using Godot;
using Godot.Collections;

using static MemoryHelpers;

[Tool]
[GlobalClass]
public partial class DrawTest: CompositorEffect {

    Rid _posBuffer;
    Rid _tcBuffer;

    // you need an index buffer AND index array
    Rid _indexBuffer;
    Rid _indexArray; 
    

	long _vertexFormat;
	Rid _vertexArray;
	
	Rid _cachedImgTexture;
	Rid _cachedDepthTexture;
	Rid _shader;

	public required RenderingDevice renderingDevice;
	Rid? _pipeline;
	Rid? _framebuffer;
	

	const int DEBUG_INSTANCES = 100;

	public DrawTest():base() {
		RenderingDevice rd = RenderingServer.GetRenderingDevice();
		ArgumentNullException.ThrowIfNull(rd);
		_Init(rd);
	}

	static Rid MakePipeline(RenderingDevice rd, long vertexFormat, Rid shader, Rid framebuffer) {
		var framebufferFmt = rd.FramebufferGetFormat(framebuffer);
		return rd.RenderPipelineCreate(
			shader, 
			framebufferFmt,
			vertexFormat,
			RenderingDevice.RenderPrimitive.Triangles,
			new RDPipelineRasterizationState() {
				CullMode = RenderingDevice.PolygonCullMode.Back,
			},
			new RDPipelineMultisampleState() {
				EnableSampleShading = false,
				SampleCount = RenderingDevice.TextureSamples.Samples1,
				MinSampleShading = 1f,
			},
			new RDPipelineDepthStencilState() {
				EnableDepthTest = true,
			},
			new RDPipelineColorBlendState() {
				Attachments = new Array<RDPipelineColorBlendStateAttachment> () {
					new(), 
				},
			}
		);
	}

    public void _Init(RenderingDevice rd) {
    	renderingDevice = rd;
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
		_posBuffer = renderingDevice.VertexBufferCreate(
			(uint)(SizeOf<Vector3>() * verts.Length), 
			verts.AsBytes(),
			0
		 );
		_tcBuffer = renderingDevice.VertexBufferCreate(
			(uint)(SizeOf<Vector2>() * texcoords.Length),
			texcoords.AsBytes(),
			0
		);
		_indexBuffer = renderingDevice.IndexBufferCreate(
			(uint)ids.Length, 
			RenderingDevice.IndexBufferFormat.Uint32, 
			ids.AsBytes(), 
			false, 
			0
		);

		_indexArray = renderingDevice.IndexArrayCreate(
			_indexBuffer, 0, (uint)ids.Length
		);
		_vertexFormat = renderingDevice.VertexFormatCreate(
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
		_vertexArray = renderingDevice.VertexArrayCreate((uint)verts.Length, _vertexFormat, 
			new Array<Rid> { _posBuffer, _tcBuffer }
		);
					
		var shaderFile = GD.Load<RDShaderFile>("res://shaders/draw_test.glsl");
		var spv = shaderFile.GetSpirV();
		_shader = rd.ShaderCreateFromSpirV(spv);
    }

    public override void _Notification(int what) {
    	if (what == NotificationPredelete) {
			Span<Rid?> rids = stackalloc Rid?[] {
				_posBuffer, _tcBuffer, _indexBuffer, _pipeline, _vertexArray, _framebuffer, _indexArray
    		};
    		foreach (var rid in rids) {
    			if (rid is {} id && id.IsValid) {
    				renderingDevice.FreeRid(id);
    			}
    		}
    	}
    }

    public override void _RenderCallback(int callbackType, RenderData renderData) {
    	if (renderingDevice is {} rd) {

    		if (callbackType == (int)EffectCallbackTypeEnum.PostTransparent) {
    			if (renderData.GetRenderSceneBuffers() is RenderSceneBuffersRD sb) {
    				var size = sb.GetInternalSize();
    				if (size.X > 0 && size.Y > 0) {

    					// need framebuffer to create the pipeline, so we do both lazily.
    					// refresh FBO if stale
    					var imgTexture = sb.GetColorTexture();
    					var depthTexture = sb.GetDepthTexture();
    					if (imgTexture != _cachedImgTexture || depthTexture != _cachedDepthTexture) {
    						if (_framebuffer is {} oldFB) {
    								if (rd.FramebufferIsValid(oldFB)) {
    								rd.FreeRid(oldFB);
    							}
    						}

    						// cache the color and depth targets, and invalidate the framebuffer and pipeline
    						_cachedImgTexture = imgTexture;
    						_cachedDepthTexture = depthTexture;
    						_framebuffer = null;
    						
							if (_pipeline is {} pipeline) {
								renderingDevice.FreeRid(pipeline);	
								_pipeline = null;
							}
    					}
    					_framebuffer ??= rd.FramebufferCreate(new Array<Rid>{ _cachedImgTexture, _cachedDepthTexture });
	    				if (_framebuffer is {} fb) {
	    					_pipeline ??= MakePipeline(renderingDevice, _vertexFormat, _shader, fb);
	    					if (_pipeline is {} pipeline) {

						    	rd.DrawCommandBeginLabel("Test draw!", new(1f, 1f, 1f, 1f));

						    	// seems like you always start a draw list with a framebuffer, which means you can't just start drawing on arbitrary textures?
						    	var drawList = rd.DrawListBegin(fb);
						    	rd.DrawListBindRenderPipeline(drawList, pipeline);
						    	rd.DrawListBindVertexArray(drawList, _vertexArray);
						    	rd.DrawListBindIndexArray(drawList, _indexArray);
						    	rd.DrawListDraw(drawList, true, 1);
						    	rd.DrawListEnd();
			    				rd.DrawCommandEndLabel();

	    					}
	    				}
    				}
    			} 
    		}
    	}
    }
}