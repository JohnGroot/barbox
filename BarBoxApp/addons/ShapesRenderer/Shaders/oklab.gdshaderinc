//By Björn Ottosson
//https://bottosson.github.io/posts/oklab
//Shader functions adapted by "mattz"
//https://www.shadertoy.com/view/WtccD7

vec3 oklab_from_linear(vec3 linear)
{
    const mat3 im1 = mat3(
        vec3(0.4121656120, 0.5362752080, 0.0514575653),
        vec3(0.2118591070, 0.6807189584, 0.1074065790),
        vec3(0.0883097947, 0.2818474174, 0.6302613616)
    );

    const mat3 im2 = mat3(
        vec3(+0.2104542553, +0.7936177850, -0.0040720468),
        vec3(+1.9779984951, -2.4285922050, +0.4505937099),
        vec3(+0.0259040371, +0.7827717662, -0.8086757660)
    );

    vec3 lms = im1 * linear;

    return im2 * (sign(lms) * pow(abs(lms), vec3(1.0/3.0)));
}

vec3 linear_from_oklab(vec3 oklab)
{
    const mat3 m1 = mat3(
        vec3(+1.000000000, +0.396337777, +0.215803757),
        vec3(+1.000000000, -0.105561346, -0.063854173),
        vec3(+1.000000000, -0.089484178, -1.291485548)
    );

    const mat3 m2 = mat3(
        vec3(+4.076724529, -3.307216883, +0.230759054),
        vec3(-1.268143773, +2.609332323, -0.341134429),
        vec3(-0.004111989, -0.703476310, +1.706862569)
    );
    vec3 lms = m1 * oklab;

    return m2 * (lms * lms * lms);
}


//By Inigo Quilez, under MIT license
//https://www.shadertoy.com/view/ttcyRS
vec3 oklab_mix(vec3 lin1, vec3 lin2, float a)
{
    // https://bottosson.github.io/posts/oklab
    const mat3 kCONEtoLMS = mat3(
        vec3(0.4121656120, 0.5362752080, 0.0514575653),
        vec3(0.2118591070, 0.6807189584, 0.1074065790),
        vec3(0.0883097947, 0.2818474174, 0.6302613616)
    );
    const mat3 kLMStoCONE = mat3(
        vec3(4.0767245293, -3.3072168827, 0.2307590544),
        vec3(-1.2681437731, 2.6093323231, -0.3411344290),
        vec3(-0.0041119885, -0.7034763098, 1.7068625689)
    );

    // rgb to cone (arg of pow can't be negative)
    vec3 lms1 = pow( kCONEtoLMS*lin1, vec3(1.0/3.0) );
    vec3 lms2 = pow( kCONEtoLMS*lin2, vec3(1.0/3.0) );
    // lerp
    vec3 lms = mix( lms1, lms2, a );
    // gain in the middle (no oklab anymore, but looks better?)
    lms *= 1.0+0.2*a*(1.0-a);
    // cone to rgb
    return kLMStoCONE*(lms*lms*lms);
}