# blender -b -P frames_to_mp4.py -- <autotest out dir>
# Turns every frames_<name>/ JPEG sequence the autotest captured into <name>.mp4 (H.264, 60 fps, 960x540) using
# Blender's bundled FFmpeg, so no separate encoder is needed. Frame folders are left in place.
import bpy, sys, os
out_dir = sys.argv[sys.argv.index('--') + 1]
for name in sorted(os.listdir(out_dir)):
    src = os.path.join(out_dir, name)
    if not (name.startswith('frames_') and os.path.isdir(src)): continue
    files = sorted(f for f in os.listdir(src) if f.endswith('.jpg'))
    if not files: continue
    sc = bpy.data.scenes.new(name)
    sc.render.fps = 60; sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = 960, 540, 100
    sc.sequence_editor_create()
    strips = sc.sequence_editor.strips if hasattr(sc.sequence_editor, 'strips') else sc.sequence_editor.sequences
    st = strips.new_image(name='seq', filepath=os.path.join(src, files[0]), channel=1, frame_start=1)
    for f in files[1:]: st.elements.append(f)
    # the captures are 1600x900: scale the strip to the 960x540 output
    st.transform.scale_x = st.transform.scale_y = 960 / 1600
    sc.frame_start, sc.frame_end = 1, len(files)
    r = sc.render
    if hasattr(r.image_settings, 'media_type'): r.image_settings.media_type = 'VIDEO'   # Blender 5
    r.image_settings.file_format = 'FFMPEG'
    r.ffmpeg.format = 'MPEG4'; r.ffmpeg.codec = 'H264'; r.ffmpeg.constant_rate_factor = 'HIGH'
    r.filepath = os.path.join(out_dir, name[len('frames_'):] + '.mp4')
    with bpy.context.temp_override(scene=sc):
        bpy.ops.render.render(animation=True, scene=sc.name)
    print('VIDEO', r.filepath, len(files), 'frames')
