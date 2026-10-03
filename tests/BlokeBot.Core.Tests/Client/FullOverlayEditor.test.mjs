import test from 'node:test';
import * as core from '../../../src/BlokeBot.Core/wwwroot/Features/Overlays/Full/Editor/FullOverlayEditorCore.js';
import { sourcePreservation, focusedOverlap, coveringAndPartialOverlap, staleAtomicAndAdjacent, deletionAndIdentity, incompleteAndAmbiguous } from './FullOverlayEditorJourneys.mjs';

test('Visual spans preserve authored source through unrelated focused edits', () => sourcePreservation(core));
test('Newer overlap stays and focused undo/redo can retry after other history changes', () => focusedOverlap(core));
test('Covering and partial source rewrites restore provenance without unrelated buffer restore', () => coveringAndPartialOverlap(core));
test('Stale and multi-buffer conflicts are atomic while adjacent grouped spans remain undoable', () => staleAtomicAndAdjacent(core));
test('Deletion and same-value newer edits do not hide operation identity', () => deletionAndIdentity(core));
test('Ambiguous attributes and incomplete CSS keep authored source available', () => incompleteAndAmbiguous(core));
