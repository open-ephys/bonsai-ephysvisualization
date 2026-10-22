import WorkflowContainer from "./workflow.js"

// Figures are labeled in markdown with an id on their image, ![alt](src){#fig-name}, and referenced with an
// empty link, [](#fig-name), as LaTeX's \label and \ref are. Numbers follow the figures' order on the page, so
// moving a figure renumbers every reference to it. A figure holding a video is a Video, numbered apart from the
// Figures, since a reader looks for something to play rather than to view.
function labelFigures() {
    const labels = new Map();
    const counts = { Figure: 0, Video: 0 };
    document.querySelectorAll("article figure").forEach(figure => {
        const kind = figure.querySelector("video") ? "Video" : "Figure";
        const name = `${kind} ${++counts[kind]}`;
        const labeled = figure.querySelector("[id]");
        if (labeled) {
            // NB: on the figure, so that a reference scrolls to the whole figure rather than to its image.
            figure.id = labeled.id;
            labeled.removeAttribute("id");
            labels.set(figure.id, name);
        }

        const caption = figure.querySelector("figcaption");
        if (caption) {
            const label = document.createElement("span");
            label.className = "figure-label";
            label.textContent = `${name}. `;
            caption.prepend(label);
        }
    });

    document.querySelectorAll('article a[href^="#"]').forEach(link => {
        const name = labels.get(decodeURIComponent(link.hash.slice(1)));
        if (name && link.textContent.trim() === "")
            link.textContent = name;
    });
}

export default {
    defaultTheme: 'light',
    iconLinks: [
        {
            icon: 'github',
            href: 'https://github.com/open-ephys/bonsai-ephysvisualization',
            title: 'GitHub'
        },
        {
            icon: 'bluesky',
            href: 'https://bsky.app/profile/open-ephys.org',
            title: 'Bluesky'
        },
        {
            icon: 'discord',
            href: 'https://discord.gg/WXAx2URNQU',
            title: 'Discord'
        }
    ],
    start: () => {
        WorkflowContainer.init();
        labelFigures();
    }
}
