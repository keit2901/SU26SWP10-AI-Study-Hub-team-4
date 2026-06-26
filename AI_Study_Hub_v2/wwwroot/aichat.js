// AI Chat helper functions
window.scrollChatToBottom = function (anchorId) {
    var el = document.getElementById(anchorId);
    if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'end' });
    }
};
