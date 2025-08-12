#[vertex]

#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inTexcoord;

layout(location = 0) out noperspective vec2 texcoord;
layout(location = 1) out vec4 color_A;
layout(location = 2) out vec4 color_B;
layout(location = 3) out noperspective vec4 segment_data;


struct LineInfo {
    int points_start;
    int points_len;
    int colors_start;
    int colors_len;
    int widths_start;
    int widths_len;
    int padding_;
    int padding__;
};

layout(push_constant, std430) uniform pc {
    mat4 u_viewProj;
    vec4 u_screenParams;
} push_constants;

layout(binding = 0, set = 0, std430) readonly buffer Line_Points {
    float elems[];
} line_points;

layout(binding = 1, set = 0, std430) readonly buffer Line_IDs {
    uint elems[];
} line_ids;

layout(binding = 2, set = 0, std430) readonly buffer Line_Colors {
    vec4 elems[];
} line_colors;

layout(binding = 3, set = 0, std430) readonly buffer Line_Widths {
    float elems[];
} line_widths;

layout(binding = 4, set = 0, std430) readonly buffer Line_Info_Buffer {
    LineInfo elems[];
} line_info_buffer;

void main() {

    int segment_id = gl_InstanceIndex;

    uint i0 = uint(segment_id) * 3u;
    uint i1 = i0 + 3u;

    // std 430 requires 16 byte alignment, so positions are a packed float array instead of vec3s
    vec3 ctrl_pt0 = vec3(line_points.elems[i0+0], line_points.elems[i0+1], line_points.elems[i0+2]);
    vec3 ctrl_pt1 = vec3(line_points.elems[i1+0], line_points.elems[i1+1], line_points.elems[i1+2]);

    uint line_id_0 = line_ids.elems[segment_id];
    uint line_id_1 = line_ids.elems[segment_id + 1];

    if (line_id_0 != line_id_1) {
        gl_Position = vec4(0); // degenerate triangle, we don't want to connect lines together
        return;
    }
    texcoord = inTexcoord;

    LineInfo line_info = line_info_buffer.elems[line_id_0];
    float t = float((segment_id - line_info.points_start) + texcoord.x) / float(line_info.points_len);

    float start_t = float(segment_id - line_info.points_start) + texcoord.x;
    float end_t = start_t + (1.0 / line_info.points_len);

    uint color_id_A, color_id_B;
    if (line_info.colors_len <= 1) {
        color_id_A = line_info.colors_start;
        color_id_B = line_info.colors_start;
    }
    else {
        color_id_A = uint(floor(line_info.colors_start + t * (line_info.colors_len - 1)));
        color_id_B = color_id_A + 1;
    }
    color_A = line_colors.elems[color_id_A];
    color_B = line_colors.elems[color_id_B];

    uint width_id0 = 0;
    uint width_id1 = 0;
    if (line_info.widths_len <= 1) {
        width_id0 = line_info.widths_start;
        width_id1 = line_info.widths_start;
    }
    else {
        width_id0 = uint(floor(line_info.widths_start + t * (line_info.widths_len - 1)));
        width_id1 = width_id0 + 1;
    }

    float maxSize = max(push_constants.u_screenParams.x, push_constants.u_screenParams.y);
    float ratio = maxSize / 1080.0; // TODO consider just making this based on height?
    float start_width = mix(line_widths.elems[width_id0], line_widths.elems[width_id1], start_t) * ratio;
    float end_width = mix(line_widths.elems[width_id0], line_widths.elems[width_id1], end_t) * ratio;

    int instance_id = gl_InstanceIndex;

    vec4 p = vec4(inPosition.xy, 0.0, 1.0);

    vec3 p0 = ctrl_pt0.xyz;
    vec3 p1 = ctrl_pt1.xyz;

    vec4 p0_clip = push_constants.u_viewProj * vec4(p0, 1.0);
    vec4 p1_clip = push_constants.u_viewProj * vec4(p1, 1.0);
    vec4 pos_clip = mix(p0_clip, p1_clip, texcoord.x);

    vec2 p0_ndc = p0_clip.xy / p0_clip.w;
    vec2 p1_ndc = p1_clip.xy / p1_clip.w;
    
    vec2 p0_ss = (p0_ndc * 0.5 + 0.5) * push_constants.u_screenParams.xy;
    vec2 p1_ss = (p1_ndc * 0.5 + 0.5) * push_constants.u_screenParams.xy;
    vec2 dir_ss = normalize(p0_ss.xy - p1_ss.xy);

    // extend the ends to account for overlap
    p0_ss += dir_ss * start_width;
    p1_ss -= dir_ss * end_width;
    vec2 vec_ss = (p0_ss.xy - p1_ss.xy);
    float segment_len = length(vec_ss);

    if (segment_len < 0.001) { // this is in pixels
        gl_Position = vec4(0); // degenerate triangle, discard this segment entirely
        return;
    }

    vec2 pos_ss = mix(p0_ss, p1_ss, texcoord.x);
    vec2 extrude = vec2(-dir_ss.y, dir_ss.x);
    float width = mix(start_width, end_width, t);

    // inflate the segment based on pixel width
    pos_ss += extrude * (texcoord.y - 0.5) * width;
    vec2 new_ndc = (pos_ss / push_constants.u_screenParams.xy) * 2.0 - 1.0;

    pos_clip.xy = new_ndc * pos_clip.w;

    // vec4 offset = vec4(0.0, texcoord.y * 0.2, 0.0, 0.0);
    // gl_Position = mix(p0_clip + offset, p1_clip + offset, texcoord.x);

    gl_Position = pos_clip;

    // gl_Position.xy += line_points.positions[instance_id].xy;
    // color = line_colors.elems[instance_id];

    segment_data = vec4(
        t,
        ((start_width) / segment_len),
        1.0 - ((end_width) / segment_len),
        0
    );
}

#[fragment]



#version 450

#include "oklab.glsl"
layout(location = 0) in noperspective vec2 texcoord;
layout(location = 1) in vec4 color_A;
layout(location = 2) in vec4 color_B;
layout(location = 3) in noperspective vec4 segment_data;

layout(location = 0) out vec4 outColor;

float unlerp(float x, float a, float b) {
    return (x - a) / (b - a);
}


void main() {
    outColor.rgb = oklab_mix(color_A.rgb, color_B.rgb, segment_data.x);
    outColor.a = 1.0;
    if (texcoord.x < segment_data.y) {
        vec2 uv = texcoord.xy;
        uv.x = unlerp(uv.x, segment_data.y, 0.0);
        vec2 mp = vec2(0.0, 0.5);
        if (length(uv - mp) > 0.5) {
            discard;
        }
    }

    if (texcoord.x > segment_data.z) {
        vec2 uv = texcoord.xy;
        uv.x = unlerp(uv.x, segment_data.z, 1.0);
        vec2 mp = vec2(0.0, 0.5);
        if (length(uv - mp) > 0.5) {
            discard;
        }
    }
}