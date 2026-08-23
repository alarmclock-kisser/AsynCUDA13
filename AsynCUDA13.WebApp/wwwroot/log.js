window.downloadTextFile = (fileName, content) => {
    const blob = new Blob([content], { type: "text/plain;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
};

window.scrollToBottom = (element) => {
    if (element && element.scrollToBottom) {
        element.scrollToBottom();
    } else if (element) {
        element.scrollTop = element.scrollHeight;
    }
};

window.sidebarExpandedWidth = 280;

window.setSidebarWidth = (width) => {
    const sidebar = document.querySelector('.app-sidebar');
    if (!sidebar) return;

    sidebar.style.width = width + 'px';
    const shell = sidebar.closest('.app-shell');
    if (shell) {
        shell.style.gridTemplateColumns = width + 'px minmax(0, 1fr)';
    }
};

window.toggleSidebarCollapse = (collapsed) => {
    const width = collapsed ? 60 : window.sidebarExpandedWidth;
    window.setSidebarWidth(width);
};

window.initSidebarResize = (handleElement, sidebarElement) => {
    if (!handleElement || !sidebarElement) return;

    const MIN_WIDTH = 100;
    const MAX_WIDTH = 600;

    let isDragging = false;
    let startX = 0;
    let startWidth = 0;

    const onMouseDown = (e) => {
        e.preventDefault();
        isDragging = true;
        startX = e.clientX;
        startWidth = sidebarElement.getBoundingClientRect().width;
        document.body.style.userSelect = 'none';
        document.body.style.cursor = 'ew-resize';

        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
    };

    const onMouseMove = (e) => {
        if (!isDragging) return;
        let newWidth = startWidth + (e.clientX - startX);
        newWidth = Math.max(MIN_WIDTH, Math.min(MAX_WIDTH, newWidth));

        window.sidebarExpandedWidth = newWidth;
        window.setSidebarWidth(newWidth);
    };

    const onMouseUp = () => {
        if (!isDragging) return;
        isDragging = false;
        document.body.style.userSelect = '';
        document.body.style.cursor = '';
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
    };

    handleElement.addEventListener('mousedown', onMouseDown);
};
