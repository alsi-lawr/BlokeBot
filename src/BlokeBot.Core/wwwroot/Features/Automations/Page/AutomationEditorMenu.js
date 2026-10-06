import { createEditorMenu } from '../../../Components/EditorContextMenu.js';
import { contextSelection } from './AutomationFlowCanvas.js';

export function createAutomationMenu(root, dotnet) {
    return createEditorMenu(root, {
        accepts: target => !!target.closest('[data-automation-canvas]'),
        fallback: () => root.querySelector('[data-automation-canvas]') ?? root.querySelector('[data-editor-menu-open]'),
        async prepare(target, point) {
            const canvas = root.querySelector('[data-automation-canvas]');
            const selection = canvas ? contextSelection(canvas, target, point) : {nodeIds:null,edgeId:null};
            if (!selection) return null;
            return dotnet.invokeMethodAsync('PrepareEditorMenuAsync', selection.nodeIds, selection.edgeId);
        },
        current: captured => root.dataset.editorMenuContext === captured.context,
        execute: (action, captured) => dotnet.invokeMethodAsync('ApplyEditorMenuAsync', action, captured.invocation)
    });
}
