(function () {
    const root = document.getElementById('chatRoot');
    if (!root) return;

    const contactsUrl = root.dataset.contactsUrl;
    const messagesUrlBase = root.dataset.messagesUrl;
    const sendUrl = root.dataset.sendUrl;
    const rightsUrl = root.dataset.rightsUrl;
    const clearChatUrl = root.dataset.clearChatUrl;
    const deleteMessageUrl = root.dataset.deleteMessageUrl;
    const deleteChatUrl = root.dataset.deleteChatUrl;
    const reactUrl = root.dataset.reactUrl;
    const starUrl = root.dataset.starUrl;
    const pinUrl = root.dataset.pinUrl;
    const currentUserId = root.dataset.currentUserId;
    const isWidget = root.classList.contains('chat-widget-mode');

    // Clear/Delete rights (chat.clear / chat.delete) - fetched once on load
    // from ChatController.Rights(), which reads Role Wise Rights / User Wise
    // Rights for the signed-in user. Buttons stay hidden until this resolves.
    let chatRights = { canClear: false, canDelete: false };

    const contactList = document.getElementById('chatContactList');
    const searchInput = document.getElementById('chatSearchInput');
    const emptyState = document.getElementById('chatEmptyState');
    const threadPane = document.getElementById('chatThreadPane');
    const threadActive = document.getElementById('chatThreadActive');
    const backBtn = document.getElementById('chatBackBtn');
    const threadName = document.getElementById('chatThreadName');
    const threadAvatar = document.getElementById('chatThreadAvatar');
    const threadStatus = document.getElementById('chatThreadStatus');
    const messagesBox = document.getElementById('chatMessages');
    const sendForm = document.getElementById('chatSendForm');
    const messageInput = document.getElementById('chatMessageInput');
    const attachBtn = document.getElementById('chatAttachBtn');
    const attachmentInput = document.getElementById('chatAttachmentInput');
    const attachmentPreview = document.getElementById('chatAttachmentPreview');
    const attachmentPreviewName = document.getElementById('chatAttachmentPreviewName');
    const attachmentPreviewRemove = document.getElementById('chatAttachmentPreviewRemove');
    const dropOverlay = document.getElementById('chatDropOverlay');
    const tokenInput = document.querySelector('#chatAntiForgeryForm input[name="__RequestVerificationToken"]');

    // Change 3 (2026-08-06): settings panel (theme / font size / quick emoji)
    const settingsToggleBtn = document.getElementById('chatSettingsToggleBtn');
    const settingsPanel = document.getElementById('chatSettingsPanel');
    const settingsCloseBtn = document.getElementById('chatSettingsCloseBtn');
    const themeGrid = document.getElementById('chatThemeGrid');
    const fontToggle = document.getElementById('chatFontToggle');
    const emojiGrid = document.getElementById('chatEmojiGrid');
    const scopeToggle = document.getElementById('chatScopeToggle');
    const scopeHint = document.getElementById('chatScopeHint');
    const dangerZone = document.getElementById('chatDangerZone');
    const clearChatBtn = document.getElementById('chatClearChatBtn');
    const deleteChatBtn = document.getElementById('chatDeleteChatBtn');

    // ADDED (2026-08-18): WhatsApp-style message action elements.
    const msgMenu = document.getElementById('chatMsgMenu');
    const msgMenuBackdrop = document.getElementById('chatMsgMenuBackdrop');
    const msgMenuReactions = document.getElementById('chatMsgMenuReactions');
    // ADDED (2026-08-22): "+" opens the full picker so any emoji can be a
    // reaction, not just the 6 quick ones; "More emojis..." does the same
    // for inserting an emoji into the message text.
    const moreEmojiBtn = document.getElementById('chatMsgMenuMoreEmoji');
    const emojiGridMoreBtn = document.getElementById('chatEmojiGridMore');
    const emojiPicker = document.getElementById('chatEmojiPicker');
    const emojiPickerBackdrop = document.getElementById('chatEmojiPickerBackdrop');
    const emojiPickerSearch = document.getElementById('chatEmojiPickerSearch');
    const emojiPickerTabs = document.getElementById('chatEmojiPickerTabs');
    const emojiPickerBody = document.getElementById('chatEmojiPickerBody');
    const replyPreview = document.getElementById('chatReplyPreview');
    const replyPreviewSender = document.getElementById('chatReplyPreviewSender');
    const replyPreviewText = document.getElementById('chatReplyPreviewText');
    const replyPreviewCancel = document.getElementById('chatReplyPreviewCancel');
    const pinnedBanner = document.getElementById('chatPinnedBanner');
    const pinnedBannerText = document.getElementById('chatPinnedBannerText');
    const pinnedBannerUnpin = document.getElementById('chatPinnedBannerUnpin');
    const selectBar = document.getElementById('chatSelectBar');
    const selectCount = document.getElementById('chatSelectCount');
    const selectCancelBtn = document.getElementById('chatSelectCancelBtn');
    const selectForwardBtn = document.getElementById('chatSelectForwardBtn');
    const selectDeleteBtn = document.getElementById('chatSelectDeleteBtn');
    const forwardModalEl = document.getElementById('chatForwardModal');
    const forwardContactList = document.getElementById('chatForwardContactList');
    const forwardSearch = document.getElementById('chatForwardSearch');
    const forwardSendBtn = document.getElementById('chatForwardSendBtn');
    const forwardSelectedCount = document.getElementById('chatForwardSelectedCount');
    const forwardTargetIds = new Set();

    // Floating launcher (only present on non-Chat pages, see _Layout.cshtml)
    const fabBtn = document.getElementById('chatFabBtn');
    const fabPanel = document.getElementById('chatFabPanel');
    const fabBackdrop = document.getElementById('chatFabBackdrop');
    const fabCloseBtn = document.getElementById('chatFabCloseBtn');

    let activeContactId = null;
    let activeContactName = null;
    let messagePollTimer = null;
    let lastMessageIdSeen = null;
    let latestContacts = [];
    let dragDepth = 0;

    // ADDED (2026-08-18): WhatsApp-style message action state.
    let latestMessages = [];              // full message objects from the last refresh, keyed by id via messageById()
    let pendingReplyTo = null;            // { id, senderName, preview } - set while composing a reply
    let activeMenuMessageId = null;       // message the open ...-menu currently targets
    let selectMode = false;               // multi-select mode (Select action) on/off
    const selectedMessageIds = new Set(); // message ids checked while in select mode
    let forwardMessageIds = [];           // message id(s) queued for the forward modal

    function csrfToken() { return tokenInput ? tokenInput.value : ''; }

    function escapeHtml(text) {
        return (text || '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    function initials(name) { return (name || '?').trim().substring(0, 1).toUpperCase() || '?'; }

    function messageById(id) { return latestMessages.find(x => x.id === id); }

    // Collapses a { userId: emoji } map into "👍 2  ❤️ 1" style chips.
    function renderReactionChips(reactions) {
        const values = Object.values(reactions || {});
        if (!values.length) return '';
        const counts = {};
        values.forEach(e => { counts[e] = (counts[e] || 0) + 1; });
        const chips = Object.keys(counts).map(e => `<span class="chat-reaction-chip">${e}${counts[e] > 1 ? ` ${counts[e]}` : ''}</span>`).join('');
        return `<div class="chat-bubble-reactions">${chips}</div>`;
    }

    function renderBubble(m) {
        const mine = m.isMine;
        const attachmentHtml = m.attachmentUrl
            ? `<div class="chat-attachment"><a href="${m.attachmentUrl}" target="_blank" rel="noopener"><i class="bi bi-paperclip"></i> ${escapeHtml(m.attachmentName || 'Attachment')}</a></div>`
            : '';
        const textHtml = m.message ? `<div>${escapeHtml(m.message).replace(/\n/g, '<br/>')}</div>` : '';
        // Particular-message delete: only rendered when the signed-in user has
        // the "chat.delete" right (Role Wise Rights / User Wise Rights).
        const deleteHtml = chatRights.canDelete
            ? `<button type="button" class="chat-bubble-delete-btn" data-message-id="${m.id}" title="Delete this message"><i class="bi bi-trash3"></i></button>`
            : '';
        const menuBtn = `<button type="button" class="chat-bubble-menu-btn" data-message-id="${m.id}" title="More"><i class="bi bi-chevron-down"></i></button>`;
        const forwardedHtml = m.isForwarded ? `<div class="chat-bubble-forwarded"><i class="bi bi-arrow-90deg-right"></i> Forwarded</div>` : '';
        const replyHtml = m.replyToMessageId
            ? `<div class="chat-bubble-reply" data-jump-to="${m.replyToMessageId}"><strong>${escapeHtml(m.replyToSenderName || 'Message')}</strong><span>${escapeHtml(m.replyToPreview || '')}</span></div>`
            : '';
        const starHtml = m.isStarredByMe ? `<i class="bi bi-star-fill chat-bubble-star" title="Starred"></i>` : '';
        const pinHtml = m.isPinned ? `<i class="bi bi-pin-angle-fill chat-bubble-pin-icon" title="Pinned"></i>` : '';
        const reactionsHtml = renderReactionChips(m.reactions);
        return `<div class="chat-bubble-row ${mine ? 'mine' : ''}" data-message-id="${m.id}">
            <label class="chat-bubble-select-check"><input type="checkbox" data-select-check /></label>
            <div class="chat-bubble ${mine ? 'chat-bubble-mine' : 'chat-bubble-theirs'}${m.isPinned ? ' chat-bubble-is-pinned' : ''}" data-message-id="${m.id}">
                ${menuBtn}${deleteHtml}${forwardedHtml}${replyHtml}${textHtml}${attachmentHtml}<div class="chat-bubble-time">${starHtml}${m.sentDate}${pinHtml}</div>${reactionsHtml}
            </div>
        </div>`;
    }

    // ---- Contact list: always rendered fully from the JSON endpoint, so this
    // works identically whether the list starts empty (floating widget) or
    // server-rendered (kept empty now too - see _ChatWidget.cshtml note). ----
    function renderContacts(contacts) {
        latestContacts = contacts || [];
        const term = (searchInput?.value || '').trim().toLowerCase();
        if (!latestContacts.length) {
            contactList.innerHTML = '<li class="chat-contact-empty text-muted small p-3">No other active users found yet.</li>';
            return;
        }
        contactList.innerHTML = latestContacts.map(c => {
            const preview = c.userId === activeContactId ? (c.role || '') : (c.lastMessage || c.role || '');
            const timeText = c.lastMessageDate ? new Date(c.lastMessageDate).toLocaleString([], { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : '';
            const showBadge = c.unreadCount > 0 && c.userId !== activeContactId;
            const isActive = c.userId === activeContactId;
            const matches = !term || (c.fullName || '').toLowerCase().includes(term);
            return `<li class="chat-contact${isActive ? ' active' : ''}" style="${matches ? '' : 'display:none'}" data-user-id="${c.userId}" data-user-name="${escapeHtml(c.fullName)}" data-search="${escapeHtml((c.fullName || '').toLowerCase())}">
                <div class="chat-contact-avatar">${escapeHtml(initials(c.fullName))}<span class="chat-status-dot ${c.isOnline ? 'online' : 'offline'}"></span></div>
                <div class="chat-contact-info">
                    <div class="chat-contact-top"><strong>${escapeHtml(c.fullName)}</strong><span class="chat-contact-time" data-last-time>${timeText}</span></div>
                    <div class="chat-contact-bottom"><small class="chat-contact-preview" data-last-message>${escapeHtml(preview)}</small>${showBadge ? `<span class="chat-unread-badge" data-unread-badge>${c.unreadCount > 99 ? '99+' : c.unreadCount}</span>` : ''}</div>
                </div>
            </li>`;
        }).join('');
    }

    async function loadContacts() {
        try {
            const res = await fetch(contactsUrl, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) return;
            const contacts = await res.json();
            renderContacts(contacts);
        } catch { /* ignore transient network errors */ }
    }

    // ---- Mobile / widget "slide-over" behaviour, with a viewport-height fix ----
    // Older approach relied on CSS `vh`/`dvh`, which several Android WebViews only
    // recompute after some other reflow (e.g. typing in the search box) - that's
    // why the input bar/attach button used to stay hidden until the list was
    // filtered. We now size the sliding panel from window.innerHeight in JS and
    // resync on resize/orientation change, so it is correct from the first paint.
    // 2026-08-02: the drawer used to force single-pane "mobile" behaviour at
    // every screen size (`isWidget ||`). Now that desktop/tablet widths get
    // their own wider two-pane layout (see chat-system.css), the slide-over
    // is only needed when the viewport itself is phone-width.
    function isMobileLayout() { return window.matchMedia('(max-width: 767px)').matches; }
    // Only the page-mode thread pane goes `position: fixed` against the real
    // viewport on narrow screens - that's the one that needs a JS-computed
    // pixel height. The widget drawer sizes itself through normal nested
    // layout (see chat-fab-panel-body/.chat-shell height:100% chain) and
    // never depended on vh/dvh, so it's left alone here.
    function needsHeightSync() { return !isWidget && window.matchMedia('(max-width: 767px)').matches; }

    function syncPanelHeight() {
        if (!needsHeightSync()) { threadPane.style.height = ''; return; }
        const h = window.innerHeight + 'px';
        threadPane.style.height = h;
        // Force a reflow on stubborn WebViews so the new height actually paints.
        requestAnimationFrame(() => { threadPane.style.height = h; });
    }

    function openThreadPanel() {
        if (!isMobileLayout()) return;
        root.classList.add('chat-thread-open');
        syncPanelHeight();
        document.body.style.overflow = 'hidden';
    }

    function closeThreadPanel() {
        root.classList.remove('chat-thread-open');
        document.body.style.overflow = isWidget && fabPanel?.classList.contains('open') ? 'hidden' : '';
        closeSettingsPanel();
    }

    window.addEventListener('resize', () => {
        syncPanelHeight();
        if (!isMobileLayout()) closeThreadPanel();
    });
    window.addEventListener('orientationchange', syncPanelHeight);

    async function openConversation(userId, userName) {
        activeContactId = userId;
        activeContactName = userName;
        lastMessageIdSeen = null;
        closeSettingsPanel();
        closeMsgMenu();
        cancelReply();
        exitSelectMode();
        applyTheme(resolveTheme(userId));
        applyFontSize(resolveFontSize(userId));
        applyScopeUI();
        updateDangerZoneUI();
        emptyState.classList.add('d-none');
        threadActive.classList.remove('d-none');
        openThreadPanel();
        threadName.textContent = userName;
        threadAvatar.textContent = initials(userName);
        threadStatus.textContent = '';
        contactList.querySelectorAll('.chat-contact').forEach(li => li.classList.toggle('active', li.dataset.userId === userId));
        const badge = contactList.querySelector(`.chat-contact[data-user-id="${userId}"] [data-unread-badge]`);
        if (badge) badge.remove();
        await refreshMessages(true);
        clearInterval(messagePollTimer);
        messagePollTimer = setInterval(() => refreshMessages(false), 4000);
    }

    async function refreshMessages(scrollToBottom, force) {
        if (!activeContactId) return;
        try {
            const res = await fetch(`${messagesUrlBase}?withUserId=${encodeURIComponent(activeContactId)}`, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) return;
            const messages = await res.json();
            const latestId = messages.length ? messages[messages.length - 1].id : null;
            // `force` (2026-08-18): after a reaction/star/pin action the newest message id
            // doesn't change, so the old "nothing new, skip re-render" short-circuit would
            // hide the update - bypass it whenever the caller knows something changed.
            if (latestId === lastMessageIdSeen && !scrollToBottom && !force) return;
            lastMessageIdSeen = latestId;
            latestMessages = messages;
            messagesBox.innerHTML = messages.map(renderBubble).join('') || '<div class="text-muted small text-center p-4">No messages yet. Say hello!</div>';
            if (scrollToBottom) messagesBox.scrollTop = messagesBox.scrollHeight;
            renderPinnedBanner();
            applySelectModeUI();
        } catch { /* ignore transient network errors */ }
    }

    // ---- Clear Chat / Delete Chat (user & role wise, via chat.clear / chat.delete) ----
    async function loadChatRights() {
        if (!rightsUrl) return;
        try {
            const res = await fetch(rightsUrl, { cache: 'no-store', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) return;
            const data = await res.json();
            chatRights = { canClear: !!data.canClear, canDelete: !!data.canDelete };
        } catch { /* ignore - buttons simply stay hidden */ }
        updateDangerZoneUI();
    }

    function updateDangerZoneUI() {
        const anyRight = chatRights.canClear || chatRights.canDelete;
        dangerZone?.classList.toggle('d-none', !anyRight || !activeContactId);
        clearChatBtn?.classList.toggle('d-none', !chatRights.canClear);
        deleteChatBtn?.classList.toggle('d-none', !chatRights.canDelete);
    }

    clearChatBtn?.addEventListener('click', async () => {
        if (!activeContactId || !chatRights.canClear) return;
        if (!confirm(`Clear this chat with ${activeContactName || 'this user'}? It will only disappear from your own view - the other person still sees it.`)) return;
        try {
            const formData = new FormData();
            formData.set('otherUserId', activeContactId);
            formData.set('__RequestVerificationToken', csrfToken());
            const res = await fetch(clearChatUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) { alert('Could not clear this chat.'); return; }
            lastMessageIdSeen = null;
            messagesBox.innerHTML = '<div class="text-muted small text-center p-4">No messages yet. Say hello!</div>';
            closeSettingsPanel();
            loadContacts();
        } catch { alert('Could not clear this chat. Please check your connection and try again.'); }
    });

    deleteChatBtn?.addEventListener('click', async () => {
        if (!activeContactId || !chatRights.canDelete) return;
        if (!confirm(`Permanently delete ALL chat with ${activeContactName || 'this user'}? This removes it for both people and cannot be undone.`)) return;
        try {
            const formData = new FormData();
            formData.set('otherUserId', activeContactId);
            formData.set('__RequestVerificationToken', csrfToken());
            const res = await fetch(deleteChatUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) { alert('Could not delete this chat.'); return; }
            lastMessageIdSeen = null;
            messagesBox.innerHTML = '<div class="text-muted small text-center p-4">No messages yet. Say hello!</div>';
            closeSettingsPanel();
            loadContacts();
        } catch { alert('Could not delete this chat. Please check your connection and try again.'); }
    });

    // Particular-message delete: event delegation on the message list, since
    // bubbles are re-rendered on every poll.
    messagesBox?.addEventListener('click', async (e) => {
        const btn = e.target.closest('.chat-bubble-delete-btn');
        if (!btn || !chatRights.canDelete) return;
        const messageId = btn.dataset.messageId;
        if (!messageId) return;
        if (!confirm('Delete this message? This cannot be undone.')) return;
        try {
            const formData = new FormData();
            formData.set('messageId', messageId);
            formData.set('__RequestVerificationToken', csrfToken());
            const res = await fetch(deleteMessageUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) { alert('Could not delete this message.'); return; }
            await refreshMessages(true);
            loadContacts();
        } catch { alert('Could not delete this message. Please check your connection and try again.'); }
    });

    // ==============================================================
    // ADDED (2026-08-18): WhatsApp-style message actions
    // React / Reply / Forward / Copy / Star / Pin / Select / Delete,
    // opened from a long-press (touch), right-click (mouse) or the
    // small "chevron" button that sits on every bubble.
    // ==============================================================

    async function deleteMessageById(messageId) {
        const formData = new FormData();
        formData.set('messageId', messageId);
        formData.set('__RequestVerificationToken', csrfToken());
        const res = await fetch(deleteMessageUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        return res.ok;
    }

    // ---- "..." action menu: open / close / position ----
    function closeMsgMenu() {
        msgMenu?.classList.add('d-none');
        msgMenuBackdrop?.classList.add('d-none');
        activeMenuMessageId = null;
    }

    function openMsgMenu(messageId, anchorEl, evt) {
        const m = messageById(messageId);
        if (!m || !msgMenu) return;
        activeMenuMessageId = messageId;

        // Reflect this message's current state in the menu (star/pin labels,
        // which reaction - if any - is highlighted, delete only if allowed).
        const starLabel = msgMenu.querySelector('[data-star-label]');
        if (starLabel) starLabel.textContent = m.isStarredByMe ? 'Unstar' : 'Star';
        const pinLabel = msgMenu.querySelector('[data-pin-label]');
        if (pinLabel) pinLabel.textContent = m.isPinned ? 'Unpin' : 'Pin';
        msgMenu.querySelector('[data-action="delete"]')?.classList.toggle('d-none', !chatRights.canDelete);
        refreshQuickReactionRow();
        const myReaction = (m.reactions || {})[currentUserId];
        msgMenuReactions?.querySelectorAll('button[data-emoji]').forEach(btn => {
            btn.classList.toggle('active', !!myReaction && btn.dataset.emoji === myReaction);
        });

        msgMenu.classList.remove('d-none');
        msgMenuBackdrop?.classList.remove('d-none');

        // Position near the tap/click point, clamped inside the viewport.
        const menuWidth = msgMenu.offsetWidth || 230;
        const menuHeight = msgMenu.offsetHeight || 280;
        const point = evt?.touches?.[0] || evt?.changedTouches?.[0] || evt || {};
        const anchorRect = anchorEl?.getBoundingClientRect();
        let x = (point.clientX ?? anchorRect?.left ?? 0);
        let y = (point.clientY ?? anchorRect?.top ?? 0);
        x = Math.min(Math.max(8, x), window.innerWidth - menuWidth - 8);
        y = Math.min(Math.max(8, y), window.innerHeight - menuHeight - 8);
        msgMenu.style.left = `${x}px`;
        msgMenu.style.top = `${y}px`;
    }

    msgMenuBackdrop?.addEventListener('click', closeMsgMenu);
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeMsgMenu(); });

    // ---- ADDED (2026-08-22): full emoji picker + "remembers your regular
    // emojis" ----------------------------------------------------------
    // Not limited to the 6 fixed reaction emojis / 24 fixed quick-insert
    // emojis any more: chatMsgMenuMoreEmoji ("+") and chatEmojiGridMore
    // ("More emojis...") open a picker covering every category in
    // chat-emoji-data.js (window.CHAT_EMOJI_CATEGORIES), with search.
    // Usage is remembered per-device (localStorage) and the two quick rows
    // are rebuilt with your most-used emoji first every time you react or
    // insert one.
    const EMOJI_CATEGORIES = window.CHAT_EMOJI_CATEGORIES || [];
    const EMOJI_FREQ_KEY = 'profitnx-chat-emoji-freq';
    // Snapshot the original hard-coded sets once, before we start rewriting
    // them - they stay as the fallback/fill-in ordering once usage history
    // exists.
    const DEFAULT_REACTIONS = Array.from(msgMenuReactions?.querySelectorAll('button[data-emoji]') || []).map(b => b.dataset.emoji);
    const DEFAULT_QUICK_GRID = Array.from(emojiGrid?.querySelectorAll('.chat-emoji-btn') || []).map(b => b.textContent);

    function loadEmojiFreq() {
        try { return JSON.parse(localStorage.getItem(EMOJI_FREQ_KEY) || '{}'); } catch { return {}; }
    }
    function bumpEmojiFreq(emoji) {
        if (!emoji) return;
        try {
            const freq = loadEmojiFreq();
            const entry = freq[emoji] || { count: 0, last: 0 };
            entry.count += 1;
            entry.last = Date.now();
            freq[emoji] = entry;
            localStorage.setItem(EMOJI_FREQ_KEY, JSON.stringify(freq));
        } catch { /* storage unavailable/full - regular row stays as-is */ }
        refreshQuickReactionRow();
        refreshQuickEmojiGrid();
    }
    function topFrequentEmojis(limit) {
        const freq = loadEmojiFreq();
        return Object.keys(freq)
            .sort((a, b) => (freq[b].count - freq[a].count) || (freq[b].last - freq[a].last))
            .slice(0, limit);
    }

    // Rebuilds the 6-button quick-reaction row (in the "..." message menu):
    // most-used emoji first, then the original defaults fill any remaining
    // slots so it's never empty for a first-time user.
    function refreshQuickReactionRow() {
        if (!msgMenuReactions) return;
        const merged = [];
        topFrequentEmojis(6).concat(DEFAULT_REACTIONS).forEach(e => { if (e && !merged.includes(e)) merged.push(e); });
        msgMenuReactions.querySelectorAll('button[data-emoji]').forEach(btn => btn.remove());
        merged.slice(0, 6).forEach(emoji => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.dataset.emoji = emoji;
            btn.title = emoji;
            btn.textContent = emoji;
            msgMenuReactions.insertBefore(btn, moreEmojiBtn);
        });
    }

    // Same idea for the "Quick emoji" insert grid in the settings panel.
    function refreshQuickEmojiGrid() {
        if (!emojiGrid) return;
        const merged = [];
        topFrequentEmojis(24).concat(DEFAULT_QUICK_GRID).forEach(e => { if (e && !merged.includes(e)) merged.push(e); });
        emojiGrid.innerHTML = '';
        merged.slice(0, 24).forEach(emoji => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'chat-emoji-btn';
            btn.textContent = emoji;
            emojiGrid.appendChild(btn);
        });
    }

    async function sendReaction(messageId, emoji) {
        if (!messageId || !emoji || !reactUrl) return;
        try {
            const formData = new FormData();
            formData.set('messageId', messageId);
            formData.set('emoji', emoji);
            formData.set('__RequestVerificationToken', csrfToken());
            await fetch(reactUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            await refreshMessages(false, true);
        } catch { /* ignore transient network errors */ }
        bumpEmojiFreq(emoji);
    }

    function insertEmojiIntoInput(emoji) {
        if (!messageInput) return;
        const start = messageInput.selectionStart ?? messageInput.value.length;
        const end = messageInput.selectionEnd ?? messageInput.value.length;
        messageInput.value = messageInput.value.slice(0, start) + emoji + messageInput.value.slice(end);
        const caret = start + emoji.length;
        messageInput.focus();
        messageInput.setSelectionRange(caret, caret);
    }

    let emojiPickerMode = null;      // 'react' | 'insert'
    let emojiPickerMessageId = null;
    let activeEmojiTab = '0';

    function renderEmojiPickerTabs() {
        if (!emojiPickerTabs) return;
        emojiPickerTabs.innerHTML = '';
        const tabs = [];
        if (topFrequentEmojis(1).length) tabs.push({ key: 'frequent', icon: '🕒', label: 'Frequent' });
        EMOJI_CATEGORIES.forEach((cat, i) => tabs.push({ key: String(i), icon: cat.icon, label: cat.name }));
        tabs.forEach((tab, idx) => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'chat-emoji-picker-tab';
            btn.dataset.tab = tab.key;
            btn.title = tab.label;
            btn.textContent = tab.icon;
            if (idx === 0) btn.classList.add('active');
            emojiPickerTabs.appendChild(btn);
        });
        activeEmojiTab = tabs[0]?.key || '0';
    }

    function appendEmojiGroup(label, list) {
        if (!emojiPickerBody || !list.length) return;
        const heading = document.createElement('div');
        heading.className = 'chat-emoji-picker-heading';
        heading.textContent = label;
        emojiPickerBody.appendChild(heading);
        const grid = document.createElement('div');
        grid.className = 'chat-emoji-picker-grid';
        list.forEach(emoji => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'chat-emoji-picker-emoji';
            btn.textContent = emoji;
            grid.appendChild(btn);
        });
        emojiPickerBody.appendChild(grid);
    }

    function renderEmojiPickerBody(tabKey, searchTerm) {
        if (!emojiPickerBody) return;
        emojiPickerBody.innerHTML = '';
        const term = (searchTerm || '').trim().toLowerCase();

        if (term) {
            // Search runs across every category regardless of the active tab.
            EMOJI_CATEGORIES.forEach(cat => {
                const matches = cat.items.filter(([, keywords]) => keywords.includes(term)).map(([emoji]) => emoji);
                appendEmojiGroup(cat.name, matches);
            });
            if (!emojiPickerBody.children.length) {
                const empty = document.createElement('div');
                empty.className = 'chat-emoji-picker-empty';
                empty.textContent = 'No emoji found';
                emojiPickerBody.appendChild(empty);
            }
            return;
        }

        if (tabKey === 'frequent') {
            appendEmojiGroup('Frequently used', topFrequentEmojis(24));
            return;
        }
        const cat = EMOJI_CATEGORIES[Number(tabKey)];
        if (cat) appendEmojiGroup(cat.name, cat.items.map(([emoji]) => emoji));
    }

    function openEmojiPicker(mode, opts) {
        if (!emojiPicker) return;
        emojiPickerMode = mode;
        emojiPickerMessageId = opts?.messageId || null;
        renderEmojiPickerTabs();
        if (emojiPickerSearch) emojiPickerSearch.value = '';
        renderEmojiPickerBody(activeEmojiTab, '');

        emojiPicker.classList.remove('d-none');
        emojiPickerBackdrop?.classList.remove('d-none');

        // Near the tap point for a message reaction; centred for the
        // settings-panel "insert" picker (there's no single message to anchor to).
        const evt = opts?.anchorEvt;
        const pickerWidth = emojiPicker.offsetWidth || 300;
        const pickerHeight = emojiPicker.offsetHeight || 360;
        const point = evt?.touches?.[0] || evt?.changedTouches?.[0] || evt || {};
        let x = point.clientX ?? (window.innerWidth - pickerWidth) / 2;
        let y = point.clientY ?? (window.innerHeight - pickerHeight) / 2;
        x = Math.min(Math.max(8, x), window.innerWidth - pickerWidth - 8);
        y = Math.min(Math.max(8, y), window.innerHeight - pickerHeight - 8);
        emojiPicker.style.left = `${x}px`;
        emojiPicker.style.top = `${y}px`;

        setTimeout(() => emojiPickerSearch?.focus(), 30);
    }

    function closeEmojiPicker() {
        emojiPicker?.classList.add('d-none');
        emojiPickerBackdrop?.classList.add('d-none');
        emojiPickerMode = null;
        emojiPickerMessageId = null;
    }

    moreEmojiBtn?.addEventListener('click', (e) => {
        e.stopPropagation();
        const messageId = activeMenuMessageId;
        if (!messageId) return;
        closeMsgMenu();
        openEmojiPicker('react', { messageId, anchorEvt: e });
    });
    emojiGridMoreBtn?.addEventListener('click', () => openEmojiPicker('insert'));
    emojiPickerBackdrop?.addEventListener('click', closeEmojiPicker);
    document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeEmojiPicker(); });
    emojiPickerTabs?.addEventListener('click', (e) => {
        const btn = e.target.closest('.chat-emoji-picker-tab');
        if (!btn) return;
        emojiPickerTabs.querySelectorAll('.chat-emoji-picker-tab').forEach(b => b.classList.toggle('active', b === btn));
        activeEmojiTab = btn.dataset.tab;
        if (emojiPickerSearch) emojiPickerSearch.value = '';
        renderEmojiPickerBody(activeEmojiTab, '');
    });
    emojiPickerSearch?.addEventListener('input', () => renderEmojiPickerBody(activeEmojiTab, emojiPickerSearch.value));
    emojiPickerBody?.addEventListener('click', (e) => {
        const btn = e.target.closest('.chat-emoji-picker-emoji');
        if (!btn) return;
        const emoji = btn.textContent;
        if (emojiPickerMode === 'react' && emojiPickerMessageId) {
            sendReaction(emojiPickerMessageId, emoji);
            closeEmojiPicker();
        } else if (emojiPickerMode === 'insert') {
            insertEmojiIntoInput(emoji);
            bumpEmojiFreq(emoji);
            // Left open on purpose - inserting several emoji in a row into the
            // same message shouldn't require reopening the picker each time.
        }
    });

    refreshQuickReactionRow();
    refreshQuickEmojiGrid();

    // Long-press (touch) / right-click (mouse) on a bubble, plus the visible
    // "chevron" button, all open the same menu - matches WhatsApp's own mix
    // of long-press-on-mobile / right-click-on-desktop behaviour.
    let longPressTimer = null;
    messagesBox?.addEventListener('touchstart', (e) => {
        const bubble = e.target.closest('.chat-bubble');
        if (!bubble) return;
        const row = bubble.closest('.chat-bubble-row');
        longPressTimer = setTimeout(() => openMsgMenu(row?.dataset.messageId, bubble, e), 450);
    }, { passive: true });
    ['touchend', 'touchmove', 'touchcancel'].forEach(evt => {
        messagesBox?.addEventListener(evt, () => { if (longPressTimer) { clearTimeout(longPressTimer); longPressTimer = null; } });
    });
    messagesBox?.addEventListener('contextmenu', (e) => {
        const bubble = e.target.closest('.chat-bubble');
        if (!bubble) return;
        e.preventDefault();
        openMsgMenu(bubble.closest('.chat-bubble-row')?.dataset.messageId, bubble, e);
    });

    // ---- Click delegation for: menu button, select-mode row tap, reply-strip jump ----
    messagesBox?.addEventListener('click', (e) => {
        const menuBtn = e.target.closest('.chat-bubble-menu-btn');
        if (menuBtn) { openMsgMenu(menuBtn.dataset.messageId, menuBtn.closest('.chat-bubble'), e); return; }

        const replyStrip = e.target.closest('.chat-bubble-reply');
        if (replyStrip) { jumpToMessage(replyStrip.dataset.jumpTo); return; }

        // Don't hijack the delete button or an attachment link into a selection toggle.
        if (e.target.closest('.chat-bubble-delete-btn, a')) return;

        const row = e.target.closest('.chat-bubble-row');
        if (selectMode && row?.dataset.messageId) {
            e.preventDefault();
            toggleSelected(row.dataset.messageId);
        }
    });

    function jumpToMessage(messageId) {
        if (!messageId) return;
        const target = messagesBox.querySelector(`.chat-bubble-row[data-message-id="${messageId}"]`);
        if (!target) return;
        target.scrollIntoView({ behavior: 'smooth', block: 'center' });
        target.classList.add('chat-bubble-row-highlight');
        setTimeout(() => target.classList.remove('chat-bubble-row-highlight'), 1400);
    }

    // ---- Menu action handling (list items + quick-reaction row) ----
    msgMenuReactions?.addEventListener('click', async (e) => {
        const btn = e.target.closest('button[data-emoji]');
        if (!btn || !activeMenuMessageId || !reactUrl) return;
        const messageId = activeMenuMessageId;
        closeMsgMenu();
        await sendReaction(messageId, btn.dataset.emoji);
    });

    msgMenu?.querySelector('.chat-msg-menu-list')?.addEventListener('click', async (e) => {
        const item = e.target.closest('li[data-action]');
        if (!item || !activeMenuMessageId) return;
        const messageId = activeMenuMessageId;
        const m = messageById(messageId);
        const action = item.dataset.action;
        closeMsgMenu();
        if (!m) return;

        if (action === 'reply') { startReply(m); }
        else if (action === 'forward') { openForwardModal([messageId]); }
        else if (action === 'copy') { copyMessageText(m); }
        else if (action === 'star') { await toggleStar(messageId); }
        else if (action === 'pin') { await togglePin(messageId, !m.isPinned); }
        else if (action === 'select') { enterSelectMode(messageId); }
        else if (action === 'delete') {
            if (!chatRights.canDelete) return;
            if (!confirm('Delete this message? This cannot be undone.')) return;
            if (await deleteMessageById(messageId)) { await refreshMessages(true, true); loadContacts(); }
            else alert('Could not delete this message.');
        }
    });

    function copyMessageText(m) {
        const text = m.message || (m.attachmentName ? `📎 ${m.attachmentName}` : '');
        if (!text) return;
        if (navigator.clipboard?.writeText) navigator.clipboard.writeText(text).catch(() => {});
        else { const ta = document.createElement('textarea'); ta.value = text; document.body.appendChild(ta); ta.select(); try { document.execCommand('copy'); } catch { /* ignore */ } ta.remove(); }
    }

    async function toggleStar(messageId) {
        if (!starUrl) return;
        try {
            const formData = new FormData();
            formData.set('messageId', messageId);
            formData.set('__RequestVerificationToken', csrfToken());
            await fetch(starUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            await refreshMessages(false, true);
        } catch { /* ignore transient network errors */ }
    }

    async function togglePin(messageId, pin) {
        if (!pinUrl || !activeContactId) return;
        try {
            const formData = new FormData();
            formData.set('messageId', messageId);
            formData.set('otherUserId', activeContactId);
            formData.set('pin', pin ? 'true' : 'false');
            formData.set('__RequestVerificationToken', csrfToken());
            await fetch(pinUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            await refreshMessages(false, true);
        } catch { /* ignore transient network errors */ }
    }

    function renderPinnedBanner() {
        const pinned = latestMessages.find(x => x.isPinned);
        if (!pinned) { pinnedBanner?.classList.add('d-none'); return; }
        if (pinnedBannerText) pinnedBannerText.textContent = pinned.message || (pinned.attachmentName ? `📎 ${pinned.attachmentName}` : 'Pinned message');
        pinnedBanner?.classList.remove('d-none');
        pinnedBanner?.setAttribute('data-message-id', pinned.id);
    }
    pinnedBanner?.addEventListener('click', (e) => {
        if (e.target.closest('#chatPinnedBannerUnpin')) return;
        jumpToMessage(pinnedBanner.dataset.messageId);
    });
    pinnedBannerUnpin?.addEventListener('click', async (e) => {
        e.stopPropagation();
        const id = pinnedBanner?.dataset.messageId;
        if (id) await togglePin(id, false);
    });

    // ---- Reply compose strip ----
    function startReply(m) {
        pendingReplyTo = {
            id: m.id,
            senderName: m.isMine ? 'Yourself' : (m.senderName || activeContactName || 'Message'),
            preview: m.message || (m.attachmentName ? `📎 ${m.attachmentName}` : '')
        };
        if (replyPreviewSender) replyPreviewSender.textContent = pendingReplyTo.senderName;
        if (replyPreviewText) replyPreviewText.textContent = pendingReplyTo.preview;
        replyPreview?.classList.remove('d-none');
        messageInput?.focus();
    }
    function cancelReply() {
        pendingReplyTo = null;
        replyPreview?.classList.add('d-none');
    }
    replyPreviewCancel?.addEventListener('click', cancelReply);

    // ---- Forward modal (single message from the menu, or several from Select mode) ----
    function renderForwardContacts() {
        if (!forwardContactList) return;
        const term = (forwardSearch?.value || '').trim().toLowerCase();
        forwardContactList.innerHTML = latestContacts
            .filter(c => !term || (c.fullName || '').toLowerCase().includes(term))
            .map(c => `<li class="chat-forward-contact" data-user-id="${c.userId}">
                <label>
                    <input type="checkbox" data-forward-check value="${c.userId}" ${forwardTargetIds.has(c.userId) ? 'checked' : ''} />
                    <span class="chat-forward-contact-avatar">${escapeHtml(initials(c.fullName))}</span>
                    <span class="chat-forward-contact-name">${escapeHtml(c.fullName)}</span>
                </label>
            </li>`).join('') || '<li class="text-muted small p-2">No people found.</li>';
    }

    function openForwardModal(messageIds) {
        forwardMessageIds = messageIds;
        forwardTargetIds.clear();
        if (forwardSearch) forwardSearch.value = '';
        renderForwardContacts();
        updateForwardSendState();
        if (window.bootstrap && forwardModalEl) bootstrap.Modal.getOrCreateInstance(forwardModalEl).show();
    }

    function updateForwardSendState() {
        if (forwardSelectedCount) forwardSelectedCount.textContent = `${forwardTargetIds.size} selected`;
        if (forwardSendBtn) forwardSendBtn.disabled = forwardTargetIds.size === 0 || forwardMessageIds.length === 0;
    }

    forwardSearch?.addEventListener('input', renderForwardContacts);
    forwardContactList?.addEventListener('change', (e) => {
        const check = e.target.closest('[data-forward-check]');
        if (!check) return;
        if (check.checked) forwardTargetIds.add(check.value); else forwardTargetIds.delete(check.value);
        updateForwardSendState();
    });

    async function forwardOneMessage(messageId, receiverId, receiverName) {
        const m = messageById(messageId);
        if (!m) return;
        const formData = new FormData();
        formData.set('receiverId', receiverId);
        formData.set('receiverName', receiverName || '');
        formData.set('message', m.message || '');
        formData.set('isForwarded', 'true');
        formData.set('__RequestVerificationToken', csrfToken());
        if (m.attachmentUrl) {
            // attachmentUrl is server-rendered from the stored relative path already,
            // so it doubles as the forwardAttachmentPath the Send action expects.
            formData.set('forwardAttachmentPath', m.attachmentUrl);
            formData.set('forwardAttachmentName', m.attachmentName || '');
        }
        await fetch(sendUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
    }

    forwardSendBtn?.addEventListener('click', async () => {
        if (!forwardMessageIds.length || !forwardTargetIds.size) return;
        forwardSendBtn.disabled = true;
        try {
            const contacts = latestContacts.filter(c => forwardTargetIds.has(c.userId));
            for (const contact of contacts) {
                for (const messageId of forwardMessageIds) {
                    await forwardOneMessage(messageId, contact.userId, contact.fullName);
                }
            }
            if (window.bootstrap && forwardModalEl) bootstrap.Modal.getOrCreateInstance(forwardModalEl).hide();
            exitSelectMode();
            if (forwardTargetIds.has(activeContactId)) await refreshMessages(true, true);
            loadContacts();
        } catch {
            alert('Could not forward the message. Please check your connection and try again.');
        } finally {
            updateForwardSendState();
        }
    });

    // ---- Select mode (multi-select for bulk Forward / Delete) ----
    function applySelectModeUI() {
        messagesBox?.classList.toggle('chat-select-mode', selectMode);
        threadPane?.querySelector('.chat-thread-header')?.classList.toggle('d-none', selectMode);
        selectBar?.classList.toggle('d-none', !selectMode);
        selectDeleteBtn?.classList.toggle('d-none', !chatRights.canDelete);
        messagesBox?.querySelectorAll('.chat-bubble-row').forEach(row => {
            row.classList.toggle('chat-bubble-row-selected', selectedMessageIds.has(row.dataset.messageId));
            const check = row.querySelector('[data-select-check]');
            if (check) check.checked = selectedMessageIds.has(row.dataset.messageId);
        });
        if (selectCount) selectCount.textContent = `${selectedMessageIds.size} selected`;
    }

    function enterSelectMode(firstMessageId) {
        selectMode = true;
        selectedMessageIds.clear();
        if (firstMessageId) selectedMessageIds.add(firstMessageId);
        applySelectModeUI();
    }
    function exitSelectMode() {
        selectMode = false;
        selectedMessageIds.clear();
        applySelectModeUI();
    }
    function toggleSelected(messageId) {
        if (selectedMessageIds.has(messageId)) selectedMessageIds.delete(messageId); else selectedMessageIds.add(messageId);
        if (selectedMessageIds.size === 0) { exitSelectMode(); return; }
        applySelectModeUI();
    }

    selectCancelBtn?.addEventListener('click', exitSelectMode);
    selectForwardBtn?.addEventListener('click', () => {
        if (!selectedMessageIds.size) return;
        openForwardModal([...selectedMessageIds]);
    });
    selectDeleteBtn?.addEventListener('click', async () => {
        if (!selectedMessageIds.size || !chatRights.canDelete) return;
        if (!confirm(`Delete ${selectedMessageIds.size} selected message(s)? This cannot be undone.`)) return;
        try {
            for (const id of [...selectedMessageIds]) await deleteMessageById(id);
            exitSelectMode();
            await refreshMessages(true, true);
            loadContacts();
        } catch { alert('Could not delete the selected messages. Please check your connection and try again.'); }
    });

    backBtn?.addEventListener('click', () => closeThreadPanel());

    contactList.addEventListener('click', (e) => {
        const li = e.target.closest('.chat-contact');
        if (!li || !li.dataset.userId) return;
        openConversation(li.dataset.userId, li.dataset.userName);
    });

    searchInput?.addEventListener('input', () => renderContacts(latestContacts));

    // ---- Attachments: file picker + drag-and-drop straight onto the thread ----
    function setAttachmentFile(file) {
        if (!file) return;
        const dt = new DataTransfer();
        dt.items.add(file);
        attachmentInput.files = dt.files;
        attachmentPreviewName.textContent = file.name;
        attachmentPreview.classList.remove('d-none');
    }

    attachBtn?.addEventListener('click', () => attachmentInput.click());
    attachmentInput?.addEventListener('change', () => {
        if (attachmentInput.files.length) setAttachmentFile(attachmentInput.files[0]);
    });
    attachmentPreviewRemove?.addEventListener('click', () => {
        attachmentInput.value = '';
        attachmentPreview.classList.add('d-none');
    });

    ['dragenter', 'dragover'].forEach(evt => {
        threadPane?.addEventListener(evt, (e) => {
            if (!activeContactId || !e.dataTransfer?.types?.includes('Files')) return;
            e.preventDefault();
            dragDepth++;
            threadPane.classList.add('chat-drag-over');
        });
    });
    ['dragleave', 'dragend'].forEach(evt => {
        threadPane?.addEventListener(evt, (e) => {
            e.preventDefault();
            dragDepth = Math.max(0, dragDepth - 1);
            if (dragDepth === 0) threadPane.classList.remove('chat-drag-over');
        });
    });
    threadPane?.addEventListener('drop', (e) => {
        if (!activeContactId || !e.dataTransfer?.files?.length) return;
        e.preventDefault();
        dragDepth = 0;
        threadPane.classList.remove('chat-drag-over');
        setAttachmentFile(e.dataTransfer.files[0]);
        messageInput?.focus();
    });

    messageInput?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendForm.requestSubmit();
        }
    });

    sendForm?.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (!activeContactId) return;
        const text = messageInput.value.trim();
        const file = attachmentInput.files[0];
        if (!text && !file) return;

        const formData = new FormData();
        formData.set('receiverId', activeContactId);
        formData.set('receiverName', activeContactName || '');
        formData.set('message', text);
        formData.set('__RequestVerificationToken', csrfToken());
        if (file) formData.set('attachment', file);
        // ADDED (2026-08-18): if the person tapped "Reply" on a message first,
        // carry that message's id along so the new message renders as a reply.
        if (pendingReplyTo?.id) formData.set('replyToMessageId', pendingReplyTo.id);

        messageInput.value = '';
        attachmentInput.value = '';
        attachmentPreview.classList.add('d-none');
        cancelReply();

        try {
            const res = await fetch(sendUrl, { method: 'POST', body: formData, headers: { 'X-Requested-With': 'XMLHttpRequest' } });
            if (!res.ok) {
                const errorText = await res.text();
                alert(errorText || 'Message could not be sent.');
                return;
            }
            await refreshMessages(true);
            loadContacts();
        } catch {
            alert('Message could not be sent. Please check your connection and try again.');
        }
    });

    // ---- Floating launcher (site-wide drawer) ----
    function openFab() {
        fabPanel.classList.add('open');
        fabBackdrop.classList.add('open');
        fabPanel.setAttribute('aria-hidden', 'false');
        document.body.style.overflow = 'hidden';
        syncPanelHeight();
        if (!latestContacts.length) loadContacts();
    }
    function closeFab() {
        fabPanel.classList.remove('open');
        fabBackdrop.classList.remove('open');
        fabPanel.setAttribute('aria-hidden', 'true');
        document.body.style.overflow = '';
        closeThreadPanel();
    }
    fabBtn?.addEventListener('click', () => {
        fabPanel.classList.contains('open') ? closeFab() : openFab();
    });
    fabCloseBtn?.addEventListener('click', closeFab);
    fabBackdrop?.addEventListener('click', closeFab);
    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape' && fabPanel?.classList.contains('open')) closeFab();
    });

    // ---- Change 3/4 (2026-08-06): settings panel - theme / font size / emoji,
    // with a scope toggle so choices can apply to just the open chat or as the
    // common default for every conversation. ----
    // Puts the space beside an open conversation to use instead of leaving it
    // blank. Preferences are per-browser (localStorage) and applied purely via
    // a data attribute / class on #chatRoot, which chat-system.css already
    // reads through CSS custom-property overrides - no message data, markup
    // ids or other JS hooks are touched.
    const THEME_STORAGE_KEY = 'profitnx-chat-theme';
    const FONT_STORAGE_KEY = 'profitnx-chat-font';
    const SCOPE_STORAGE_KEY = 'profitnx-chat-settings-scope';

    function chatThemeKey(contactId) { return `profitnx-chat-theme:contact:${contactId}`; }
    function chatFontKey(contactId) { return `profitnx-chat-font:contact:${contactId}`; }

    // Individual chat setting (if one was saved for this contact) wins;
    // otherwise fall back to the common "all chats" default.
    function resolveTheme(contactId) {
        try {
            if (contactId) {
                const perChat = localStorage.getItem(chatThemeKey(contactId));
                if (perChat) return perChat;
            }
            return localStorage.getItem(THEME_STORAGE_KEY) || 'aurora';
        } catch { return 'aurora'; }
    }
    function resolveFontSize(contactId) {
        try {
            if (contactId) {
                const perChat = localStorage.getItem(chatFontKey(contactId));
                if (perChat) return perChat;
            }
            return localStorage.getItem(FONT_STORAGE_KEY) || 'md';
        } catch { return 'md'; }
    }

    function applyTheme(theme) {
        const value = theme || 'aurora';
        if (value === 'aurora') root.removeAttribute('data-chat-theme');
        else root.setAttribute('data-chat-theme', value);
        themeGrid?.querySelectorAll('.chat-theme-swatch').forEach(btn => {
            btn.classList.toggle('active', (btn.dataset.theme || 'aurora') === value);
        });
    }

    function applyFontSize(size) {
        const value = size || 'md';
        root.classList.remove('chat-font-sm', 'chat-font-lg');
        if (value === 'sm' || value === 'lg') root.classList.add(`chat-font-${value}`);
        fontToggle?.querySelectorAll('.chat-font-option').forEach(btn => {
            btn.classList.toggle('active', (btn.dataset.font || 'md') === value);
        });
    }

    let settingsScope = 'all';
    try { settingsScope = localStorage.getItem(SCOPE_STORAGE_KEY) || 'all'; } catch { settingsScope = 'all'; }

    function applyScopeUI() {
        scopeToggle?.querySelectorAll('.chat-scope-option').forEach(btn => {
            const isChatOption = (btn.dataset.scope || 'all') === 'chat';
            btn.classList.toggle('active', (btn.dataset.scope || 'all') === settingsScope);
            btn.classList.toggle('chat-scope-option-disabled', isChatOption && !activeContactId);
        });
        if (scopeHint) {
            scopeHint.textContent = (settingsScope === 'chat' && activeContactId)
                ? `Theme & font apply only to ${activeContactName || 'this chat'}.`
                : !activeContactId
                    ? 'Open a conversation first to set a theme just for that chat.'
                    : 'Theme & font apply to every conversation.';
        }
    }

    function openSettingsPanel() {
        settingsPanel?.classList.add('open');
        settingsPanel?.setAttribute('aria-hidden', 'false');
        settingsToggleBtn?.classList.add('active');
        fabPanel?.classList.add('chat-fab-panel-wide');
    }

    function closeSettingsPanel() {
        settingsPanel?.classList.remove('open');
        settingsPanel?.setAttribute('aria-hidden', 'true');
        settingsToggleBtn?.classList.remove('active');
        fabPanel?.classList.remove('chat-fab-panel-wide');
    }

    applyTheme(resolveTheme(activeContactId));
    applyFontSize(resolveFontSize(activeContactId));
    applyScopeUI();

    settingsToggleBtn?.addEventListener('click', () => {
        settingsPanel?.classList.contains('open') ? closeSettingsPanel() : openSettingsPanel();
    });
    settingsCloseBtn?.addEventListener('click', closeSettingsPanel);

    scopeToggle?.addEventListener('click', (e) => {
        const btn = e.target.closest('.chat-scope-option');
        if (!btn) return;
        const scope = btn.dataset.scope || 'all';
        if (scope === 'chat' && !activeContactId) return; // nothing open to scope to yet
        settingsScope = scope;
        try { localStorage.setItem(SCOPE_STORAGE_KEY, scope); } catch { /* ignore */ }
        applyScopeUI();
    });

    themeGrid?.addEventListener('click', (e) => {
        const btn = e.target.closest('.chat-theme-swatch');
        if (!btn) return;
        const theme = btn.dataset.theme || 'aurora';
        applyTheme(theme);
        // Read the scope directly from the currently-active toggle button
        // rather than trusting the settingsScope variable in isolation -
        // this guarantees a theme is only ever saved "for this chat" when
        // that option is genuinely the one showing as selected right now.
        const liveScope = scopeToggle?.querySelector('.chat-scope-option.active')?.dataset.scope || settingsScope;
        try {
            if (liveScope === 'chat' && activeContactId) localStorage.setItem(chatThemeKey(activeContactId), theme);
            else localStorage.setItem(THEME_STORAGE_KEY, theme);
        } catch { /* ignore */ }
    });

    fontToggle?.addEventListener('click', (e) => {
        const btn = e.target.closest('.chat-font-option');
        if (!btn) return;
        const size = btn.dataset.font || 'md';
        applyFontSize(size);
        const liveScope = scopeToggle?.querySelector('.chat-scope-option.active')?.dataset.scope || settingsScope;
        try {
            if (liveScope === 'chat' && activeContactId) localStorage.setItem(chatFontKey(activeContactId), size);
            else localStorage.setItem(FONT_STORAGE_KEY, size);
        } catch { /* ignore */ }
    });

    emojiGrid?.addEventListener('click', (e) => {
        const btn = e.target.closest('.chat-emoji-btn');
        if (!btn || !messageInput) return;
        insertEmojiIntoInput(btn.textContent);
        bumpEmojiFreq(btn.textContent);
    });

    loadContacts();
    loadChatRights();
    setInterval(loadContacts, 8000);
    syncPanelHeight();
})();
