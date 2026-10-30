// A creature drawn as a 3D model (enhancements.crittermodels, docs/enhancements.md). It writes what
// uwnpc_allred writes for a sprite, so the viewport combination shades and colours it the same way:
// red the palette index, green the distance, and the object's index on the objectinfo layer.
// The model's pixels hold the creature art's 32 colour slots, not colours. A slot becomes a main
// palette index through the creature type's aux palette, as the original does with the sprites, so
// the types that share a body (the four goblins) share one model.

shader_type spatial;
render_mode blend_mix,depth_draw_opaque,cull_back,diffuse_burley,specular_disabled,unshaded;

// the slot of each pixel: a texture's red channel (a skinned model), or the vertex colour's (voxels)
uniform sampler2D slot_texture : filter_nearest, repeat_enable;
uniform bool slot_from_vertex = false;
uniform int auxpalette[32];

global uniform float cutoffdistance; // how far away the scene will render
global uniform sampler2D simpleshade: source_color,filter_nearest;//shading data for objectinfo layer. used to find darkness

uniform int objectindex_lowerbytes;
uniform int objectindex_upperbytes;

void fragment() {
	vec2 world_camera = (INV_VIEW_MATRIX * vec4(vec3(0.0), 1.0)).xz;
	vec4 a = INV_VIEW_MATRIX * vec4(VERTEX, 1.0);
	float dist = (distance(a.xz, world_camera));
	if (dist > cutoffdistance)
	{
		discard;
	}
	int slot = int(round((slot_from_vertex ? COLOR.r : texture(slot_texture, UV).r) * 255.0));
	int index = auxpalette[clamp(slot, 0, 31)];
	float r = float(index) / 255.0;	// raw, as the sprites' one-channel textures give it
	float maptouse = dist/cutoffdistance;

	switch(CAMERA_VISIBLE_LAYERS)
	{
		case 2u: //world without xfer
		{
			if ((index >= 249) && (index <= 253))
			{
				discard;	//an xfer colour draws only on the xfer camera
			}
			ALBEDO = vec3(r, maptouse, 0.0);
			break;
		}
		case 6u: // world with xfer
		{
			ALBEDO = vec3(r, maptouse, 0.0);
			break;
		}
		case 8u: //objectinfo
		{
			vec4 simple = texture(simpleshade, vec2(dist/cutoffdistance, 0.0));
			int shadelevel = ivec3(round(simple.rgb * 255.0)).r;
			if (shadelevel >= 112)
			{
				discard;
			}
			ALBEDO = vec3(0.0, float(objectindex_lowerbytes)/255.0, float(objectindex_upperbytes)/255.0);
			break;
		}
		default:
		{
			ALBEDO.r = 0.3;
			break;
		}
	}
}
