# blender -b chimesinger_grip_edit.blend -P chimesinger_grip_extract.py -- <out chimesinger_grip.json>
# Reads the hand-posed right-hand grip (finger / thumb bones and the staff bone, posed by hand in Blender) from the grip
# edit file and writes it as bone-local transforms. chimesinger_rig.py applies them to every clip in place of its
# computed fist when the JSON sits next to it.
import bpy, sys, json
out = sys.argv[sys.argv.index('--') + 1]
arm = bpy.data.objects['ChimesingerRig']
bones = list(arm.get('grip_edit_bones', []))
data = {}
for n in bones:
    pb = arm.pose.bones[n]
    data[n] = {'rot': list(pb.rotation_quaternion), 'loc': list(pb.location)}
with open(out, 'w') as fh:
    json.dump(data, fh, indent=1)
print('GRIP_SAVED', out, sorted(data))
