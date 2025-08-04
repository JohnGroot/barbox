#[vertex]

#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec2 inTexcoord;

layout(location = 0) out vec2 texcoord;

void main() {
    gl_Position = vec4(inPosition.xy, 0.5, 1.0);
    texcoord = inTexcoord;
}

#[fragment]

#version 450

layout(location = 0) in vec2 texcoord;
layout(location = 0) out vec4 outColor;

void main() {
    outColor = vec4(texcoord.xy, 0.0, 1.0);
}