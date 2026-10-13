import WorkflowContainer from "./workflow.js"

export default {
    defaultTheme: 'light',
    start: () => {
        WorkflowContainer.init();
    }
}