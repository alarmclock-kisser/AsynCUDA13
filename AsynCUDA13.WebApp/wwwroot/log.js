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
