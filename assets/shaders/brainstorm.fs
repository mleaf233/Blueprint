#if __VERSION__ > 100 || defined(GL_FRAGMENT_PRECISION_HIGH)
	#define PRECISION highp
#else
	#define PRECISION mediump
#endif

// extern  int dpi;
extern vec3 greyscale_weights;
extern int blur_amount;
extern ivec2 card_size;
extern ivec2 margin;
extern vec4 blue_low;
extern vec4 blue_high;
extern vec4 red_low;
extern vec4 red_high;
extern float blue_threshold;
extern float red_threshold;
// extern bool floating;
extern vec2 texture_size;

// Love2D/GLSL Compatibility Macros
#ifndef Image
#define Image sampler2D
#endif

#ifndef Texel
#define Texel texture2D
#endif

// Max blur loop limit (constant required for loop unrolling in some drivers)
#define MAX_BLUR 10

float greyscale(vec4 col) {
    return dot(greyscale_weights, col.rgb);
}

vec4 myTexelFetch(Image s, ivec2 c) {
    // [Fix]: Explicit cast to vec2 for coordinate calculation
    // return texelFetch(s, c * dpi, l);
    vec2 uv = (vec2(c) + vec2(0.5)) / texture_size;
    return Texel(s, uv);
}

float gaussian_blur(Image jokers_sampler, ivec2 texture_coords) {
    float col = 0.0;
    float total = 0.0;
    
    // [Fix]: Loop bounds must be constants
    for (int x = -MAX_BLUR; x <= MAX_BLUR; x++) {
        // Optimization: skip iterations outside current blur_amount
        if (x < -blur_amount || x > blur_amount) continue;

        for (int y = -MAX_BLUR; y <= MAX_BLUR; y++) {
            if (y < -blur_amount || y > blur_amount) continue;

            ivec2 offset = ivec2(x, y);
            float factor;
            
            if (blur_amount == 0) {
                factor = 1.0;
            } else {
                // [Fix]: Cast ivec2 to vec2 before dot product
                vec2 offset_f = vec2(offset);
                float dist_sq = dot(offset_f, offset_f);
                
                // [Fix]: Cast int to float for division
                float blur_f = float(blur_amount);
                factor = exp(-dist_sq / (blur_f * blur_f));
            }
            
            col += greyscale(myTexelFetch(jokers_sampler, texture_coords + offset)) * factor;
            total += factor;
        }
    }
    
    // [Fix]: Safety check for division
    if (total > 0.0) {
        col /= total;
    }
    return col;
}

#define sobel_kernel_length 6

vec2 sobel_filter(Image jokers_sampler, ivec2 texture_coords) {
    // [Fix]: Initialize array elements individually (No C-style initializer lists)
    vec3 sobel_kernelx[6];
    sobel_kernelx[0] = vec3(-1.0, -1.0, -1.0);
    sobel_kernelx[1] = vec3(-1.0,  0.0, -2.0);
    sobel_kernelx[2] = vec3(-1.0,  1.0, -1.0);
    sobel_kernelx[3] = vec3( 1.0, -1.0,  1.0);
    sobel_kernelx[4] = vec3( 1.0,  0.0,  2.0);
    sobel_kernelx[5] = vec3( 1.0,  1.0,  1.0);

    vec3 sobel_kernely[6];
    sobel_kernely[0] = vec3(-1.0, -1.0, -1.0);
    sobel_kernely[1] = vec3( 0.0, -1.0, -2.0);
    sobel_kernely[2] = vec3( 1.0, -1.0, -1.0);
    sobel_kernely[3] = vec3(-1.0,  1.0,  1.0);
    sobel_kernely[4] = vec3( 0.0,  1.0,  2.0);
    sobel_kernely[5] = vec3( 1.0,  1.0,  1.0);

    vec2 d = vec2(0.0);
    
    for (int i = 0; i < sobel_kernel_length; i++) {
        // [Fix]: extract xy as float, then cast to ivec2 for texture offset
        ivec2 offset_x = ivec2(sobel_kernelx[i].xy);
        d.x += gaussian_blur(jokers_sampler, texture_coords + offset_x) * sobel_kernelx[i].z;
        
        ivec2 offset_y = ivec2(sobel_kernely[i].xy);
        d.y += gaussian_blur(jokers_sampler, texture_coords + offset_y) * sobel_kernely[i].z;
    }
    return d;
}

#define pi 3.14159265359

float canny_edges(Image jokers_sampler, ivec2 texture_coords) {
    vec2 d = sobel_filter(jokers_sampler, texture_coords);
    float g = length(d);
    
    // [Fix]: Use atan(y, x) to avoid division by zero and handle quadrants correctly
    float t = atan(d.y, d.x);
    
    // determine where to sample from the direction of the gradient
    ivec2 offset1;
    ivec2 offset2;
    
    if (t < -0.375 * pi) {
        offset1 = ivec2(0, -1);
        offset2 = ivec2(0, 1);
    } else if (t < -0.125 * pi) {
        offset1 = ivec2(1, -1);
        offset2 = ivec2(-1, 1);
    } else if (t < 0.125 * pi) {
        offset1 = ivec2(-1, 0);
        offset2 = ivec2(1, 0);
    } else if (t < 0.375 * pi) {
        offset1 = ivec2(-1, -1);
        offset2 = ivec2(1, 1);
    } else {
        offset1 = ivec2(0, -1);
        offset2 = ivec2(0, 1);
    }
    // sample
    float g1 = length(sobel_filter(jokers_sampler, texture_coords + offset1));
    float g2 = length(sobel_filter(jokers_sampler, texture_coords + offset2));
    // if this is a local maximum
    if (g1 < g && g2 < g) {
        return g;
    } else {
        return 0.0;
    }
}

float hash12(vec2 p) {
    vec3 p3  = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

vec4 mapcol(float v, ivec2 coords) {
    // [Fix]: Cast ivec2 to vec2 for hash function
    if (v > blue_threshold) {
        return mix(blue_low, blue_high, hash12(vec2(coords)));
    } else if (v > red_threshold) {
        return mix(red_low, red_high, hash12(vec2(coords)));
    } else {
        return vec4(0.0, 0.0, 0.0, 0.0);
    }
}

// bool is_floating_edge(Image jokers_sampler, ivec2 texture_coords) {
//     if (myTexelFetch(jokers_sampler, texture_coords, 0).a < 1.0) {
//         return false;
//     }
//     for (int x = -1; x <= 1; x++) {
//         for (int y = -1; y <= 1; y++) {
//             if (myTexelFetch(jokers_sampler, texture_coords + ivec2(x, y), 0).a < 1.0) {
//                 return true;
//             }
//         }
//     }
//     return false;
// }

vec4 effect(vec4 colour, Image jokers_sampler, vec2 texture_coords, vec2 screen_coords) {
    
    // float col = greyscale(texture(jokers_sampler, texture_coords));
    // [Fix]: explicit cast to ivec2
    ivec2 absolute_texture_coords = ivec2(texture_coords * texture_size);
    // float col = gaussian_blur(jokers_sampler, absolute_texture_coords);
    // vec2 d = sobel_filter(jokers_sampler, absolute_texture_coords);
    
    float canny = canny_edges(jokers_sampler, absolute_texture_coords);
    // if (floating && is_floating_edge(jokers_sampler, absolute_texture_coords)) {
        // canny = 100.0;
    // }
    vec4 cannycol = mapcol(canny, absolute_texture_coords);
    
    // [Fix]: Convert all ints to floats for mod() compatibility
    float abs_x = float(absolute_texture_coords.x);
    float card_x = float(card_size.x);
    float margin_x = float(margin.x);
    
    // [Fix]: Use float mod
    if (mod(abs_x, card_x) < margin_x || mod(abs_x, card_x) >= card_x - margin_x) {
        cannycol = vec4(0.0);
    }

    float abs_y = float(absolute_texture_coords.y);
    float card_y = float(card_size.y);
    float margin_y = float(margin.y);
    
    if (mod(abs_y, card_y) < margin_y || mod(abs_y, card_y) >= card_y - margin_y) {
        cannycol = vec4(0.0);
    }
    
    return cannycol;
        // return vec4(cannycol.rgb, min(cannycol.a, myTexelFetch(jokers_sampler, absolute_texture_coords, 0).a));
    // } else {
    //     return cannycol;
    // }
	// return texelFetch(jokers_sampler, absolute_texture_coords, 0);
	// return texture(jokers_sampler, texture_coords);
}