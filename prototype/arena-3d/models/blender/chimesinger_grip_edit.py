# blender -b <work>/chimesinger_rig.blend -P chimesinger_grip_edit.py -- <out chimesinger_grip_edit.blend>
# Builds the hand-posing file: the rig frozen in the Idle pose with the solved grip, only the right hand's finger / thumb
# bones (index1-3, middle1-3, ring1-3, pinky1-3, thumb1-3) and the staff bone free to edit, everything else locked.
# Pose the grip by hand, save, then run chimesinger_grip_extract.py on it to write chimesinger_grip.json; the rig then
# uses that grip in place of the solved one.
import bpy, sys
out = sys.argv[sys.argv.index('--') + 1]
sc = bpy.context.scene; arm = bpy.data.objects['ChimesingerRig']
act = bpy.data.actions['Idle']; arm.animation_data.action = act
if act.slots: arm.animation_data.action_slot = act.slots[0]
sc.frame_set(1)
pose = {pb.name: (pb.location.copy(), pb.rotation_quaternion.copy()) for pb in arm.pose.bones}
arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.location, pb.rotation_quaternion = pose[pb.name]
DIGITS = ('index', 'middle', 'ring', 'pinky', 'thumb')
EDIT = ['staff'] + [n for n in pose if n.endswith('.R') and n.startswith(DIGITS)]
for pb in arm.pose.bones:
    lock = pb.name not in EDIT
    pb.lock_location = (lock or pb.name != 'staff',) * 3
    pb.lock_rotation = (lock,) * 3; pb.lock_rotation_w = lock; pb.lock_scale = (True,) * 3
    pb.bone.hide = lock
arm.show_in_front = True; arm.data.display_type = 'OCTAHEDRAL'
for o in bpy.data.objects:
    if o.type == 'MESH': o.hide_select = True
arm['grip_edit_bones'] = EDIT          # chimesinger_grip_extract.py reads these bones back
bpy.ops.wm.save_as_mainfile(filepath=out)
print('SAVED', out, len(EDIT), 'bones')
