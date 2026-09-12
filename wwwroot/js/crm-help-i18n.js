/* ProfitNx CRM — Help language (en default, gu, hi)
   Gujarati copy is written for native Gujarati speakers in Gujarat —
   natural spoken style, not machine-translated or Hindi-calqued. */
(function (global) {
  var STORAGE_KEY = 'profitnx.helpLang';

  var ui = {
    en: {
      read: 'Read Steps Aloud',
      stop: 'Stop Reading',
      video: 'Watch Video Guide',
      videoSoon: 'Video Guide (coming soon)',
      langLabel: 'Help language',
      step: 'Step',
      showSteps: 'Show steps'
    },
    gu: {
      read: 'વાંચી સંભળાવો',
      stop: 'બંધ કરો',
      video: 'વીડિયો જુઓ',
      videoSoon: 'વીડિયો ગાઇડ (જલ્દી આવશે)',
      langLabel: 'મદદની ભાષા',
      step: 'પગલું',
      showSteps: 'પગલાં જુઓ'
    },
    hi: {
      read: 'चरण जोर से पढ़ें',
      stop: 'पढ़ना बंद करें',
      video: 'वीडियो देखें',
      videoSoon: 'वीडियो गाइड (जल्द)',
      langLabel: 'सहायता भाषा',
      step: 'चरण',
      showSteps: 'चरण दिखाएँ'
    }
  };

  // Native-style Gujarati (Gujarat) — keep common CRM English terms where staff actually say them
  var byTitle = {
    'Dashboard Help': {
      gu: {
        title: 'ડેશબોર્ડ મદદ',
        steps: [
          'ઉપરના કાર્ડમાં આજના અને આ મહિનાના આંકડા જુઓ.',
          'સૌથી પહેલા Smart Focus માં દેખાતું કામ ખોલો અને પૂરું કરો.',
          'આગળ વધવા માટે ડાબી બાજુનું મેનૂ અથવા કીબોર્ડ શોર્ટકટ વાપરો.'
        ]
      },
      hi: {
        title: 'डैशबोर्ड सहायता',
        steps: [
          'ऊपर आज और इस महीने के नंबर वाले कार्ड देखें।',
          'पहले Smart Focus में दी गई आइटम खोलें और काम पूरा करें।',
          'आगे बढ़ने के लिए बाईं ओर का मेनू या कीबोर्ड शॉर्टकट उपयोग करें।'
        ]
      }
    },
    'New Inquiry Help': {
      gu: {
        title: 'નવી Inquiry મદદ',
        steps: [
          'ગ્રાહકનું નામ, ફર્મ, મોબાઇલ અને પ્રોડક્ટની વિગત ભરો.',
          'Direct Customer કે Partner માંથી સાચો વિકલ્પ પસંદ કરો.',
          'Save દબાવો. જરૂરી ફીલ્ડ ખાલી હશે તો સિસ્ટમ તરત ચેતવણી આપશે.'
        ]
      },
      hi: {
        title: 'नई इन्क्वायरी सहायता',
        steps: [
          'ग्राहक का नाम, फर्म, मोबाइल और प्रोडक्ट की जानकारी भरें।',
          'Direct Customer या Partner में से सही विकल्प चुनें।',
          'Save दबाएँ। जरूरी फील्ड खाली होने पर सिस्टम खुद चेतावनी देगा।'
        ]
      }
    },
    'Inquiry Workspace Help': {
      gu: {
        title: 'Inquiry વર્કસ્પેસ મદદ',
        steps: [
          'ઉપરના કાર્ડ — Total, Follow Up, Demo, Sold, Close — પર ક્લિક કરીને તે સ્ટેટસની યાદી જુઓ.',
          'Conversion Focus માં Hot leads અને Overdue follow-ups દેખાય છે. દરરોજ પહેલા આ કામ સાફ કરો.',
          'New Inquiry બટનથી નવી ગ્રાહક Inquiry બનાવો.',
          'ફિલ્ટરમાંથી સ્ટેટસ કે તારીખ પસંદ કરીને Go દબાવો. Clear થી બધું ફરીથી સેટ થાય છે.',
          'Status બેજ પર ક્લિક કરીને પૂરો ટાઇમલાઇન જુઓ.',
          'View, Edit અને Delete બટન તમારા અધિકાર પ્રમાણે દેખાય છે.',
          'Quick Status થી યાદીમાંથી જ સ્ટેટસ બદલી શકો છો.',
          'Forward થયેલી Inquiry પહેલા ખોલો અને Attended માર્ક કરો.'
        ]
      },
      hi: {
        title: 'इन्क्वायरी वर्कस्पेस सहायता',
        steps: [
          'ऊपर के कार्ड (Total, Follow Up, Demo, Sold, Close) पर क्लिक करके उस स्टेटस की लिस्ट देखें।',
          'Conversion Focus में Hot leads और Overdue follow-ups दिखते हैं — हर दिन पहले इन्हें क्लियर करें।',
          'New Inquiry बटन से नई ग्राहक इन्क्वायरी बनाएँ।',
          'फिल्टर से स्टेटस या तारीख चुनकर Go दबाएँ। Clear से सब रीसेट हो जाता है।',
          'Status बैज पर क्लिक करके पूरा टाइमलाइन देखें।',
          'View, Edit, Delete बटन आपके अधिकार के अनुसार दिखते हैं।',
          'Quick Status से लिस्ट में ही स्टेटस बदल सकते हैं।',
          'Forwarded इन्क्वायरी पहले खोलें और Attended मार्क करें।'
        ]
      }
    },
    'Reports Help': {
      gu: {
        title: 'રિપોર્ટ મદદ',
        steps: [
          'તારીખની રેન્જ અને જરૂરી ફિલ્ટર પસંદ કરો.',
          'AI Insights માંથી સૂચનો અને પ્રાયોરિટી જુઓ.',
          'Export ત્યારે જ કરો જ્યારે તમારી પાસે અધિકાર હોય.'
        ]
      },
      hi: {
        title: 'रिपोर्ट सहायता',
        steps: [
          'तारीख रेंज और जरूरी फिल्टर चुनें।',
          'AI Insights से सुझाव और प्राथमिकता देखें।',
          'Export सिर्फ तभी करें जब आपके पास अधिकार हो।'
        ]
      }
    },
    'Implementation & Training Help': {
      gu: {
        title: 'Implementation અને ટ્રેનિંગ મદદ',
        steps: [
          'સપોર્ટ મેમ્બર અસાઇન કરો અને ગ્રાહક સાથે તારીખ તથા સમય કન્ફર્મ કરો.',
          'દરેક ડિલિવરી દિવસ માટે શેડ્યૂલ બનાવો અને કવર થયેલા પોઇન્ટ નોંધો.',
          'બધા પેન્ડિંગ પોઇન્ટ પૂરા થયા પછી જ ટ્રેનિંગ પૂરી કરો. ફીડબેક આપમેળે ઇમેઇલ થશે.'
        ]
      },
      hi: {
        title: 'इम्प्लीमेंटेशन और ट्रेनिंग सहायता',
        steps: [
          'सपोर्ट मेंबर असाइन करें और तारीख व समय कन्फर्म करें।',
          'हर डिलीवरी दिन का शेड्यूल बनाएँ और पॉइंट नोट करें।',
          'सभी पेंडिंग पॉइंट पूरे होने के बाद ट्रेनिंग पूरा करें। फीडबैक अपने आप ईमेल होगा।'
        ]
      }
    },
    'User Wise Rights Help': {
      gu: {
        title: 'User Wise Rights મદદ',
        steps: [
          'ફક્ત જે યુઝરને અલગ અધિકાર જોઈએ તેમના માટે Separate ચાલુ કરો.',
          'Inherited યુઝર હંમેશા Role Wise Rights પ્રમાણે ચાલે છે.',
          'રોલ બદલાય તો પણ Separate યુઝરના અધિકાર ઓવરરાઇટ થતા નથી.'
        ]
      },
      hi: {
        title: 'यूजर वाइज राइट्स सहायता',
        steps: [
          'सिर्फ जिन यूजर को अलग अधिकार चाहिए उनके लिए Separate चालू करें।',
          'Inherited यूजर हमेशा Role Wise Rights के अनुसार चलते हैं।',
          'रोल बदलने पर भी Separate यूजर के अधिकार ओवरराइट नहीं होते।'
        ]
      }
    },
    'Role Wise Rights Help': {
      gu: {
        title: 'Role Wise Rights મદદ',
        steps: [
          'દરેક રોલ માટે જરૂરી પરમિશન ટિક કરો.',
          'Save કરો જેથી Inherited યુઝરને તે લાગુ પડે.',
          'Separate યુઝરની પોતાની સેટિંગ અલગ રહે છે.'
        ]
      },
      hi: {
        title: 'रोल वाइज राइट्स सहायता',
        steps: [
          'हर रोल के लिए जरूरी अनुमति चुनें।',
          'Save करें ताकि Inherited यूजर पर लागू हो।',
          'Separate यूजर अपनी सेटिंग अलग रखते हैं।'
        ]
      }
    },
    'Product Price Help': {
      gu: {
        title: 'પ્રોડક્ટ ભાવ મદદ',
        steps: [
          'Direct Customer Price એ સીધા ગ્રાહકને વેચાણ માટે છે.',
          'Partner Product Price એ પાર્ટનરનો બેઝ ભાવ છે.',
          'પાર્ટનર માર્જિન આપમેળે કપાઈ જાય છે.'
        ]
      },
      hi: {
        title: 'प्रोडक्ट कीमत सहायता',
        steps: [
          'Direct Customer Price डायरेक्ट ग्राहक को बेचने के लिए है।',
          'Partner Product Price पार्टनर का बेस रेट है।',
          'पार्टनर मार्जिन अपने आप कट जाता है।'
        ]
      }
    },
    'Stock Help': {
      gu: {
        title: 'સ્ટોક મદદ',
        steps: [
          'પાર્ટનર, પ્રોડક્ટ, બિલ અથવા લાઇસન્સથી ફિલ્ટર કરો.',
          'સ્ટોક વેલ્યૂ એટલે પાર્ટનર ભાવ માંથી માર્જિન બાદ કર્યા પછીનું મૂલ્ય.',
          'Create Stock Bill ફક્ત અધિકાર હોય ત્યારે જ વાપરો.'
        ]
      },
      hi: {
        title: 'स्टॉक सहायता',
        steps: [
          'पार्टनर, प्रोडक्ट, बिल या लाइसेंस से फिल्टर करें।',
          'स्टॉक वैल्यू = पार्टनर कीमत माइनस मार्जिन।',
          'Create Stock Bill सिर्फ अधिकार होने पर ही उपयोग करें।'
        ]
      }
    },
    'Notification Help': {
      gu: {
        title: 'નોટિફિકેશન મદદ',
        steps: [
          'દરેક વ્યક્તિ માટે Unread અલગ ગણાય છે.',
          'એલર્ટ ખોલતાં ફક્ત તમારી કોપી Read થાય છે.',
          'રિમાર્ક્સ અને ફોર્વર્ડ પણ નોટિફિકેશન બનાવે છે.'
        ]
      },
      hi: {
        title: 'नोटिफिकेशन सहायता',
        steps: [
          'हर व्यक्ति के लिए Unread अलग गिना जाता है।',
          'अलर्ट खोलने पर सिर्फ आपकी कॉपी Read होती है।',
          'रिमार्क्स और फॉरवर्ड भी नोटिफिकेशन बनाते हैं।'
        ]
      }
    },
    'CRM Help': {
      gu: {
        title: 'CRM મદદ',
        steps: [
          'કોઈપણ બટન કે ફીલ્ડ પર માઉસ રાખો તો ટૂલટિપ દેખાશે.',
          'શોર્ટકટની યાદી માટે Alt અને / એકસાથે દબાવો.',
          'મેનૂમાં હાઇલાઇટ થયેલી આઇટમ તમારું વર્તમાન પેજ બતાવે છે.'
        ]
      },
      hi: {
        title: 'CRM सहायता',
        steps: [
          'किसी भी बटन या फील्ड पर माउस रखें तो टूलटिप दिखेगी।',
          'शॉर्टकट सूची के लिए Alt और / एक साथ दबाएँ।',
          'मेनू में हाइलाइट की गई आइटम आपका वर्तमान पेज बताती है।'
        ]
      }
    }
  };

  function getLang() {
    try {
      var v = (localStorage.getItem(STORAGE_KEY) || 'en').toLowerCase();
      return (v === 'gu' || v === 'hi' || v === 'en') ? v : 'en';
    } catch (e) { return 'en'; }
  }

  function setLang(lang) {
    var v = (lang === 'gu' || lang === 'hi') ? lang : 'en';
    try { localStorage.setItem(STORAGE_KEY, v); } catch (e) { /* ignore */ }
    return v;
  }

  function uiText(lang) {
    return ui[lang] || ui.en;
  }

  function localize(help, lang) {
    lang = lang || getLang();
    if (!help) return { title: 'CRM Help', steps: [], lang: lang, videoUrl: '' };
    if (lang === 'en') {
      return { title: help.title, steps: help.steps || [], lang: 'en', videoUrl: help.videoUrl || '' };
    }
    var pack = byTitle[help.title] && byTitle[help.title][lang];
    if (pack) {
      return { title: pack.title, steps: pack.steps || help.steps || [], lang: lang, videoUrl: help.videoUrl || '' };
    }
    return { title: help.title, steps: help.steps || [], lang: lang, videoUrl: help.videoUrl || '' };
  }

  function speechLang(lang) {
    if (lang === 'gu') return 'gu-IN';
    if (lang === 'hi') return 'hi-IN';
    return 'en-IN';
  }

  // Prefer natural / Google / neural voices when the OS exposes them
  var QUALITY_HINT = /google|natural|neural|online|premium|enhanced/i;

  function rank(voices, tester) {
    var pool = voices.filter(tester);
    if (!pool.length) return null;
    var quality = pool.find(function (v) { return QUALITY_HINT.test(v.name); });
    return quality || pool[0];
  }

  function pickVoice(voices, lang) {
    voices = voices || [];
    if (lang === 'gu') {
      // Strict order: true Gujarati first, then Hindi (script-compatible), then Indian English
      return rank(voices, function (v) {
          return (/^gu([-_]|$)/i.test(v.lang) || /gujarati/i.test(v.name));
        })
        || rank(voices, function (v) {
          return (/^hi([-_]|$)/i.test(v.lang) || /hindi/i.test(v.name));
        })
        || rank(voices, function (v) {
          return /en[-_]in/i.test(v.lang) || /india/i.test(v.name);
        })
        || rank(voices, function (v) { return /^en/i.test(v.lang); })
        || null;
    }
    if (lang === 'hi') {
      return rank(voices, function (v) {
          return (/^hi([-_]|$)/i.test(v.lang) || /hindi|ravi|heera|neerja|priya/i.test(v.name));
        })
        || rank(voices, function (v) { return /en[-_]in/i.test(v.lang) || /india/i.test(v.name); })
        || rank(voices, function (v) { return /^en/i.test(v.lang); })
        || null;
    }
    return rank(voices, function (v) {
        return /en[-_]in/i.test(v.lang) || /india|neerja|ravi|heera|priya/i.test(v.name);
      })
      || rank(voices, function (v) { return /en[-_]gb/i.test(v.lang); })
      || rank(voices, function (v) { return /en[-_]us/i.test(v.lang); })
      || rank(voices, function (v) { return /^en/i.test(v.lang); })
      || null;
  }

  function hasNativeGujaratiVoice(voices) {
    voices = voices || [];
    return voices.some(function (v) {
      return /^gu([-_]|$)/i.test(v.lang) || /gujarati/i.test(v.name);
    });
  }

  function hasNativeVoice(voices, lang) {
    voices = voices || [];
    if (lang === 'gu') return hasNativeGujaratiVoice(voices);
    if (lang === 'hi') {
      return voices.some(function (v) {
        return /^hi([-_]|$)/i.test(v.lang) || /hindi/i.test(v.name);
      });
    }
    return true;
  }

  /**
   * Prepare text for natural TTS: strip UI noise, normalize punctuation,
   * keep short readable phrases (Gujarati/Hindi/English).
   */
  function prepareSpeechText(text, lang) {
    if (!text) return '';
    var t = String(text);
    // Remove symbols speech engines often misread
    t = t.replace(/[•·▪►▶←→⇒⇔—–]/g, ' ');
    t = t.replace(/[<>{}[\]\\|^=~`]/g, ' ');
    t = t.replace(/\s+/g, ' ').trim();
    // Ensure sentence end so the engine pauses
    if (lang === 'gu' || lang === 'hi') {
      if (t && !/[.।!?]$/.test(t)) t = t + (lang === 'gu' ? '.' : '।');
    } else if (t && !/[.!?]$/.test(t)) {
      t = t + '.';
    }
    return t;
  }

  /**
   * Split a long step into shorter utterances for natural pauses.
   * Keeps CRM terms intact; splits on sentence boundaries.
   */
  function splitForSpeech(text, lang) {
    var prepared = prepareSpeechText(text, lang);
    if (!prepared) return [];
    // Split on full stop / danda / question / exclamation
    var parts = prepared.split(/(?<=[.।!?])\s+/).map(function (p) { return p.trim(); }).filter(Boolean);
    if (parts.length <= 1) return [prepared];
    // Merge very short fragments
    var out = [];
    var buf = '';
    parts.forEach(function (p) {
      if (!buf) {
        buf = p;
      } else if ((buf + ' ' + p).length < 90) {
        buf = buf + ' ' + p;
      } else {
        out.push(buf);
        buf = p;
      }
    });
    if (buf) out.push(buf);
    return out.length ? out : [prepared];
  }

  global.ProfitNxHelpI18n = {
    getLang: getLang,
    setLang: setLang,
    uiText: uiText,
    localize: localize,
    speechLang: speechLang,
    pickVoice: pickVoice,
    hasNativeVoice: hasNativeVoice,
    hasNativeGujaratiVoice: hasNativeGujaratiVoice,
    prepareSpeechText: prepareSpeechText,
    splitForSpeech: splitForSpeech
  };
})(window);
