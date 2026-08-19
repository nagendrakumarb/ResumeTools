window.professionalHub = {
    // ---------------------------------------------------------------------------
    // Google AdSense Handler (Using your animation frame & visibility fix)
    // ---------------------------------------------------------------------------
    adsense: {
        init: function () {
            if (!document.getElementById("adsense-js")) {
                const script = document.createElement("script");
                script.id = "adsense-js";
                script.src = "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client=ca-pub-8487728962349258";
                script.async = true;
                script.crossOrigin = "anonymous";
                document.head.appendChild(script);
            }
        },
        push: function () {
            // Delay execution slightly to allow Blazor layout rendering to complete
            window.requestAnimationFrame(() => {
                setTimeout(() => {
                    const uninitializedSlots = document.querySelectorAll('ins.adsbygoogle:not([data-adsbygoogle-status])');

                    uninitializedSlots.forEach((slot) => {
                        // Only push if the container is currently visible in the DOM
                        if (slot.offsetWidth > 0) {
                            try {
                                (window.adsbygoogle = window.adsbygoogle || []).push({});
                            } catch (e) {
                                console.warn("AdSense push execution skipped:", e);
                            }
                        }
                    });
                }, 100);
            });
        }
    },

    // ---------------------------------------------------------------------------
    // File Download Utilities
    // ---------------------------------------------------------------------------
    files: {
        downloadBase64: function (fileName, contentType, base64) {
            try {
                const cleanBase64 = base64.includes(",") ? base64.split(",")[1] : base64;
                const binary = atob(cleanBase64);
                const bytes = new Uint8Array(binary.length);
                for (let i = 0; i < binary.length; i++) {
                    bytes[i] = binary.charCodeAt(i);
                }

                const blob = new Blob([bytes], { type: contentType });
                const url = URL.createObjectURL(blob);
                const anchor = document.createElement("a");
                anchor.href = url;
                anchor.download = fileName;
                anchor.style.display = "none";
                document.body.appendChild(anchor);
                anchor.click();
                anchor.remove();
                setTimeout(() => URL.revokeObjectURL(url), 30000);
                return true;
            } catch (error) {
                console.error("File download failed.", error);
                return false;
            }
        }
    },

    // ---------------------------------------------------------------------------
    // Image Template Analysis
    // ---------------------------------------------------------------------------
    images: {
        analyzeTemplate: function (base64, contentType) {
            return new Promise((resolve, reject) => {
                const img = new Image();
                img.onload = () => {
                    if (!img.width || !img.height) {
                        reject(new Error("Invalid image dimensions."));
                        return;
                    }

                    const width = 240;
                    const height = Math.max(1, Math.round((width * img.height) / img.width));
                    const canvas = document.createElement("canvas");
                    canvas.width = width;
                    canvas.height = height;

                    const ctx = canvas.getContext("2d", { willReadFrequently: true });
                    if (!ctx) {
                        reject(new Error("Canvas 2D context could not be initialized."));
                        return;
                    }

                    ctx.drawImage(img, 0, 0, width, height);
                    const data = ctx.getImageData(0, 0, width, height).data;

                    let r = 0, g = 0, b = 0, count = 0, ink = 0, ruleRows = 0;
                    const bands = [0, 0, 0];
                    const colorBands = [0, 0, 0];
                    const topBands = [0, 0, 0];

                    for (let y = 0; y < height; y += 2) {
                        for (let x = 0; x < width; x += 2) {
                            const i = (y * width + x) * 4;
                            const rr = data[i], gg = data[i + 1], bb = data[i + 2];
                            const max = Math.max(rr, gg, bb);
                            const min = Math.min(rr, gg, bb);
                            const saturation = max - min;

                            if (max < 238 || saturation > 22) {
                                ink++;
                                const band = Math.min(2, Math.floor(x / (width / 3)));
                                bands[band]++;
                                if (y < height * 0.18) topBands[band]++;
                            }

                            if (saturation > 35 && max < 230 && max > 45) {
                                r += rr; g += gg; b += bb; count++;
                                colorBands[Math.min(2, Math.floor(x / (width / 3)))]++;
                            }
                        }
                    }

                    for (let y = 0; y < height; y++) {
                        let longest = 0, current = 0;
                        for (let x = 0; x < width; x++) {
                            const i = (y * width + x) * 4;
                            const dark = Math.max(data[i], data[i + 1], data[i + 2]) < 150;
                            current = dark ? current + 1 : 0;
                            if (current > longest) longest = current;
                        }
                        if (longest > width * 0.48) ruleRows++;
                    }

                    const hex = n => Math.round(n).toString(16).padStart(2, "0").toUpperCase();
                    const hasColor = count > Math.max(20, ink * 0.025);
                    const accentHex = hasColor ? `${hex(r / count)}${hex(g / count)}${hex(b / count)}` : "111111";

                    const edgeMax = Math.max(bands[0], bands[2]);
                    const edgeMin = Math.min(bands[0], bands[2]);
                    const twoColumn = edgeMax > bands[1] * 1.18 && edgeMax > edgeMin * 1.2;
                    const coloredEdge = Math.max(colorBands[0], colorBands[2]);

                    const sidebar = !twoColumn ? "none" :
                        coloredEdge > count * 0.2 ? (colorBands[0] > colorBands[2] ? "left" : "right") :
                            (bands[0] < bands[2] ? "left" : "right");

                    const topTotal = topBands[0] + topBands[1] + topBands[2] || 1;
                    const centeredHeader = topBands[1] / topTotal > 0.38;

                    resolve({
                        accentHex,
                        layout: twoColumn ? "two-column" : "one-column",
                        sidebar,
                        aspectRatio: img.width / img.height,
                        typography: hasColor ? "sans-serif" : "serif",
                        sectionRules: ruleRows >= 2,
                        headerAlignment: centeredHeader ? "center" : "left",
                        density: ink / Math.max(1, Math.ceil(width / 2) * Math.ceil(height / 2))
                    });
                };

                img.onerror = () => reject(new Error("The image template could not be decoded."));
                const srcPrefix = contentType.startsWith("data:") ? "" : `data:${contentType};base64,`;
                img.src = `${srcPrefix}${base64}`;
            });
        }
    },

    // ---------------------------------------------------------------------------
    // Encrypted Job Ledger Storage
    // ---------------------------------------------------------------------------
    jobLedger: {
        fileName: "applied-jobs.phjob",

        openDb: function () {
            return new Promise((resolve, reject) => {
                const request = indexedDB.open("professional-hub", 2);
                request.onupgradeneeded = () => {
                    const db = request.result;
                    if (!db.objectStoreNames.contains("analyses"))
                        db.createObjectStore("analyses", { keyPath: "id", autoIncrement: true });
                    if (!db.objectStoreNames.contains("settings"))
                        db.createObjectStore("settings");
                    if (!db.objectStoreNames.contains("cryptoKeys"))
                        db.createObjectStore("cryptoKeys");
                };
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },

        getValue: async function (storeName, key) {
            const db = await this.openDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction(storeName, "readonly");
                const request = tx.objectStore(storeName).get(key);
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },

        setValue: async function (storeName, key, value) {
            const db = await this.openDb();
            return new Promise((resolve, reject) => {
                const tx = db.transaction(storeName, "readwrite");
                const store = tx.objectStore(storeName);
                store.put(value, key);
                tx.oncomplete = () => resolve();
                tx.onerror = () => reject(tx.error);
            });
        },

        permission: async function (handle, request) {
            if (!handle) return false;
            const options = { mode: "readwrite" };
            try {
                if ((await handle.queryPermission(options)) === "granted") return true;
                return request && (await handle.requestPermission(options)) === "granted";
            } catch (err) {
                console.warn("Permission query failed", err);
                return false;
            }
        },

        getState: async function () {
            if (!window.showDirectoryPicker) {
                return {
                    supported: false,
                    configured: false,
                    permissionGranted: false,
                    message: "Folder storage requires a Chromium browser such as Edge or Chrome."
                };
            }

            const handle = await this.getValue("settings", "jobLedgerFolder");
            if (!handle) {
                return {
                    supported: true,
                    configured: false,
                    permissionGranted: false,
                    message: "Choose a folder to store encrypted applied-job records."
                };
            }

            const granted = await this.permission(handle, false);
            return {
                supported: true,
                configured: true,
                permissionGranted: granted,
                message: granted
                    ? `Mapped folder: ${handle.name}`
                    : `Folder ${handle.name} is mapped; click Map local folder to restore permission.`
            };
        },

        chooseFolder: async function () {
            if (!window.showDirectoryPicker) {
                return { supported: false, configured: false, permissionGranted: false, message: "Folder storage requires Edge or Chrome." };
            }
            const handle = await window.showDirectoryPicker({ id: "professionalhub-applied-jobs", mode: "readwrite" });
            if (!(await this.permission(handle, true))) throw new Error("Read/write folder permission was not granted.");
            await this.setValue("settings", "jobLedgerFolder", handle);
            await this.getKey();
            return { supported: true, configured: true, permissionGranted: true, message: `Mapped folder: ${handle.name}` };
        },

        getKey: async function () {
            let key = await this.getValue("cryptoKeys", "jobLedgerAesKey");
            if (!key) {
                key = await crypto.subtle.generateKey({ name: "AES-GCM", length: 256 }, false, ["encrypt", "decrypt"]);
                await this.setValue("cryptoKeys", "jobLedgerAesKey", key);
            }
            return key;
        },

        toBase64: function (bytes) {
            let binary = "";
            const len = bytes.byteLength;
            for (let i = 0; i < len; i++) {
                binary += String.fromCharCode(bytes[i]);
            }
            return btoa(binary);
        },

        fromBase64: function (value) {
            const binary = atob(value);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            return bytes;
        },

        encrypt: async function (record, key) {
            const iv = crypto.getRandomValues(new Uint8Array(12));
            const data = new TextEncoder().encode(JSON.stringify(record));
            const cipher = new Uint8Array(await crypto.subtle.encrypt({ name: "AES-GCM", iv }, key, data));
            return { iv: this.toBase64(iv), cipher: this.toBase64(cipher) };
        },

        decrypt: async function (chunk, key) {
            const plain = await crypto.subtle.decrypt(
                { name: "AES-GCM", iv: this.fromBase64(chunk.iv) },
                key,
                this.fromBase64(chunk.cipher)
            );
            return JSON.parse(new TextDecoder().decode(plain));
        },

        encryptDocument: async function (base64, key) {
            const chunks = [];
            const size = 256 * 1024;
            for (let offset = 0, index = 0; offset < base64.length; offset += size, index++) {
                chunks.push(await this.encrypt({ index, data: base64.slice(offset, offset + size) }, key));
            }
            return chunks;
        },

        decryptDocument: async function (chunks, key) {
            const parts = [];
            for (const chunk of chunks || []) parts.push(await this.decrypt(chunk, key));
            return parts.sort((left, right) => left.index - right.index).map(part => part.data).join("");
        },

        readDateFolder: async function (directory, key) {
            try {
                const fileHandle = await directory.getFileHandle(this.fileName);
                const file = await fileHandle.getFile();
                if (!file.size) return { records: [], documents: {} };
                const envelope = JSON.parse(await file.text());
                const records = [];
                for (const chunk of envelope.chunks || []) {
                    try {
                        records.push(await this.decrypt(chunk, key));
                    } catch (error) {
                        console.warn("Skipped an unreadable applied-job chunk.", error);
                    }
                }
                return { records, documents: envelope.documents || {} };
            } catch (error) {
                if (error.name === "NotFoundError") return { records: [], documents: {} };
                throw error;
            }
        },

        readAll: async function (requestPermission) {
            const handle = await this.getValue("settings", "jobLedgerFolder");
            if (!handle) throw new Error("Map a local folder before saving or filtering applied jobs.");
            if (!(await this.permission(handle, requestPermission))) {
                throw new Error("Folder permission is required. Click Map local folder.");
            }
            const key = await this.getKey();
            const records = [];
            try {
                for await (const [name, entry] of handle.entries()) {
                    if (entry.kind === "directory" && /^\d{4}-\d{2}-\d{2}$/.test(name)) {
                        const content = await this.readDateFolder(entry, key);
                        records.push(...content.records);
                    }
                }
            } catch (error) {
                console.error("Failed reading ledger directories.", error);
            }
            return { handle, key, records };
        },

        loadFingerprints: async function () {
            const state = await this.getState();
            if (!state.configured || !state.permissionGranted) return [];
            const ledger = await this.readAll(false);
            return [...new Set(ledger.records.map(record => record.fingerprint).filter(Boolean))];
        },

        list: async function () {
            const ledger = await this.readAll(false);
            const results = [];
            for await (const [date, entry] of ledger.handle.entries()) {
                if (entry.kind !== "directory" || !/^\d{4}-\d{2}-\d{2}$/.test(date)) continue;
                const content = await this.readDateFolder(entry, ledger.key);
                for (const record of content.records) {
                    const document = content.documents[record.fingerprint];
                    results.push({
                        fingerprint: record.fingerprint,
                        providerJobId: record.providerJobId || "",
                        title: record.title || "",
                        company: record.company || "",
                        location: record.location || "",
                        url: record.url || "",
                        source: record.source || "",
                        postedAt: record.postedAt || "",
                        postedDate: record.postedDate || date,
                        skills: record.skills || [],
                        status: record.status || "",
                        recordedAt: record.recordedAt || "",
                        hasResume: !!document,
                        resumeFileName: document?.fileName || ""
                    });
                }
            }
            return results.sort((left, right) => (right.recordedAt || "").localeCompare(left.recordedAt || ""));
        },

        getResume: async function (postedDate, fingerprint) {
            const ledger = await this.readAll(false);
            const directory = await ledger.handle.getDirectoryHandle(postedDate);
            const content = await this.readDateFolder(directory, ledger.key);
            const document = content.documents[fingerprint];
            if (!document) throw new Error("No tailored resume is stored for this job.");
            return {
                fileName: document.fileName || "Job-Tailored-Resume.docx",
                base64: await this.decryptDocument(document.chunks, ledger.key)
            };
        },

        save: async function (records) {
            const ledger = await this.readAll(true);
            const incomingRecords = Array.isArray(records)
                ? records
                : records && typeof records === "object"
                    ? [records]
                    : [];

            if (!incomingRecords.length) {
                throw new Error("No valid job records were supplied for saving.");
            }

            const groups = new Map();
            for (const record of incomingRecords) {
                const date = /^\d{4}-\d{2}-\d{2}$/.test(record.postedDate)
                    ? record.postedDate
                    : new Date().toISOString().slice(0, 10);
                if (!groups.has(date)) groups.set(date, []);
                groups.get(date).push(record);
            }

            let saved = 0, updated = 0, total = 0;
            const verifiedFiles = [];

            for (const [date, incoming] of groups) {
                const directory = await ledger.handle.getDirectoryHandle(date, { create: true });
                const content = await this.readDateFolder(directory, ledger.key);
                const byId = new Map(content.records.map(record => [record.fingerprint, record]));
                const documents = content.documents || {};

                for (const record of incoming) {
                    if (byId.has(record.fingerprint)) updated++;
                    else saved++;

                    const storedRecord = { ...record };
                    delete storedRecord.resumeBase64;
                    delete storedRecord.resumeFileName;

                    byId.set(record.fingerprint, storedRecord);

                    if (record.resumeBase64) {
                        documents[record.fingerprint] = {
                            fileName: record.resumeFileName || "Job-Tailored-Resume.docx",
                            contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                            chunks: await this.encryptDocument(record.resumeBase64, ledger.key)
                        };
                    }
                }

                const chunks = [];
                for (const record of byId.values()) {
                    chunks.push(await this.encrypt(record, ledger.key));
                }

                const fileHandle = await directory.getFileHandle(this.fileName, { create: true });
                const writable = await fileHandle.createWritable();
                await writable.write(JSON.stringify({ version: 2, algorithm: "AES-GCM-256", chunks, documents }));
                await writable.close();

                const writtenFile = await fileHandle.getFile();
                if (!writtenFile.size) {
                    throw new Error(`The browser created ${date}/${this.fileName}, but the file is empty.`);
                }

                const verification = await this.readDateFolder(directory, ledger.key);
                const verifiedById = new Map(verification.records.map(record => [record.fingerprint, record]));
                const missing = incoming.filter(record => {
                    const stored = verifiedById.get(record.fingerprint);
                    return !stored || stored.status !== record.status;
                });

                if (missing.length) {
                    throw new Error(`The encrypted ledger could not be verified after writing ${date}/${this.fileName}.`);
                }

                verifiedFiles.push(`${date}/${this.fileName}`);
                total += byId.size;
            }

            return {
                saved,
                updated,
                total,
                message: `${saved} job(s) saved and ${updated} updated. Verified: ${verifiedFiles.join(", ")}.`
            };
        }
    },

    // ---------------------------------------------------------------------------
    // History Store
    // ---------------------------------------------------------------------------
    history: {
        open: function () {
            return new Promise((resolve, reject) => {
                const request = indexedDB.open("professional-hub", 2);
                request.onupgradeneeded = () => {
                    const db = request.result;
                    if (!db.objectStoreNames.contains("analyses")) {
                        db.createObjectStore("analyses", { keyPath: "id", autoIncrement: true });
                    }
                };
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        },

        save: async function (record) {
            const db = await this.open();
            return new Promise((resolve, reject) => {
                const tx = db.transaction("analyses", "readwrite");
                tx.objectStore("analyses").add(record);
                tx.oncomplete = () => resolve();
                tx.onerror = () => reject(tx.error);
            });
        },

        getAll: async function () {
            const db = await this.open();
            return new Promise((resolve, reject) => {
                const tx = db.transaction("analyses", "readonly");
                const request = tx.objectStore("analyses").getAll();
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
            });
        }
    }
};

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => {
        window.professionalHub.adsense.init();
    });
} else {
    window.professionalHub.adsense.init();
}