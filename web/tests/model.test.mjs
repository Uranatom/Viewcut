import test from 'node:test';
import assert from 'node:assert/strict';
import { Editor,validateName,outputName } from '../model.js';
test('clip history groups a drag or multiple deletion into one undo',() => {
  const e = new Editor(); const first = e.add(1,3), second = e.add(5,8); e.rename(first.id,'日落');
  const before = e.snapshot(); first.start = .5; first.start = .2; first.end = 4; e.record(before);
  e.undo(); assert.equal(e.clips[0].start,1); e.redo(); assert.equal(e.clips[0].end,4);
  e.selection = new Set([first.id,second.id]); e.remove(); assert.equal(e.clips.length,0); e.undo(); assert.equal(e.clips.length,2);
  e.rename(first.id,'山峰'); assert.equal(e.redoStack.length,0);
});
test('portable names and case-insensitive collisions',() => {
  const clips = [{id:'1',name:'日落'},{id:'2',name:'Sunset'}];
  assert.equal(validateName('sunset',clips,'1'),'duplicateName');
  for (const name of ['CON','COM1','LPT².txt','bad/name','x.','x ','a\0b']) assert.ok(validateName(name,[],'1'),name);
  assert.equal(outputName('camera.MOV',{number:2,name:'日落'}),'日落.mov');
  assert.equal(outputName('camera.mp4',{number:3,name:''}),'camera_003.mp4');
});
