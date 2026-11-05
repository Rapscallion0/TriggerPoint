# TriggerPoint Core Workflows 🛠

This guide provides step-by-step instructions for everyday tasks in TriggerPoint: creating executable actions, designing rich text and plain text snippets, using dynamic placeholder tokens, working with the Spotlight Command Palette and Quick Calculator, building cursor menus, and scoping actions to specific applications.

---

## 1. Creating App & Command Actions

Shell actions launch executables, open documents, execute command-line scripts, or open URLs in specific browsers.

![Shell Action Editor Panel](images/workflow-shell-action-editor.png)
<!-- SCREENSHOT REQUIRED: Action details panel configured for a Shell action ("Launch Visual Studio Code") showing the Executable Path box with "code", Arguments box populated with "C:\Projects\TriggerPoint", Working Directory set to "C:\Projects", "Run as Administrator" checkbox unchecked, Display Target dropdown set to "Display 2 (Secondary)", and a green "Path Valid" validation status chip. -->

### Step-by-Step Setup
1. Click **+ Add Action** on the toolbar and select **App / Command** (or press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>A</kbd>).
2. Enter a **Name** (e.g., `Open Terminal`) and an optional **Description**.
3. Record a **Global Hotkey** using the Hotkey Recorder (e.g., <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd>).
4. In the **Command / Executable Path** box, enter the target:
   - Executable on the system `PATH` (e.g., `wt.exe`, `notepad.exe`, `code`).
   - Absolute file path (e.g., `C:\Program Files\Git\git-bash.exe`).
   - Web URL (e.g., `https://github.com/Rapscallion0/TriggerPoint`).
   - *Tip*: Click the **Browse (`...`)** button or drag any executable or shortcut (`.lnk`) directly into the window.
5. In the **Arguments** box, specify any optional command-line flags (e.g., `-d "C:\Source"` or `--incognito`).
6. In the **Working Directory** box, specify the directory from which the process starts. If left blank, TriggerPoint automatically defaults to the directory containing the target executable.
7. Optional Execution Flags:
   - **Run as Administrator**: Checks will launch the command with elevated Windows UAC privileges. When enabled, a `🛡 ADMIN` shield badge appears on the action card.
   - **Display Target**: Directs the launched window to a specific monitor (`Primary Monitor`, `Display 1`, `Display 2`, or `Follow Mouse Cursor`). TriggerPoint moves and positions the window automatically post-launch.
8. Click **Save** (<kbd>Ctrl</kbd> + <kbd>S</kbd>) or press **Test Action** (<kbd>F5</kbd>) to verify execution immediately.

---

## 2. Plain Text & Rich Text Snippets

TriggerPoint's dynamic snippet engine can paste plain keystrokes or rich, beautifully formatted content into any active window (text editors, email clients, word processors, web forms, or chat apps).

### Plain Text vs. Rich Text Format Toggle
When editing a Snippet action, switch between **Plain Text** and **Rich Text** using the format toggle buttons at the top of the editor:

```
[ Plain Text ]  [ Rich Text ✨ ]
```

- **Plain Text**: Optimized for code, terminal commands, markdown, and simple unformatted strings. Sends simulated keystrokes or plain clipboard paste.
- **Rich Text**: Full visual WYSIWYG editor supporting font sizes, bold, italics, underlines, custom colors, highlights, bulleted and numbered lists, and paragraph alignment.

---

### A. Plain Text Snippets with Contextual Token Assistant

![Plain Text Snippet Editor](images/workflow-plain-snippet-tokens.png)
<!-- SCREENSHOT REQUIRED: Plain Text snippet editor showing a template containing "Meeting Notes - {date:MMMM d, yyyy}\nAttendee: {username}\nAction Items:\n- {cursor}", with the Contextual Token Assistant pill bar visible below suggesting format options for {date}. Underneath, the Live Expansion Preview card displays the evaluated result with real-time char and token counts. -->

1. Select **Plain Text** mode.
2. Type or paste your template into the snippet box.
3. Use the **Contextual Token Assistant** pill bar below the editor to insert dynamic placeholders with a single click.
4. Watch the **Live Expansion Preview** card at the bottom update in real time, showing the exact rendered output, token count, and character length.

---

### B. Rich Text Snippets & Visual Formatting Ribbon

![Rich Text Snippet Editor in Paper Canvas Mode](images/workflow-rich-text-editor.png)
<!-- SCREENSHOT REQUIRED: Rich Text snippet editor in Paper Canvas mode (clean white background #FFFFFF with dark slate text #1E293B). The formatting ribbon shows: 11pt font size dropdown, Bold active, custom blue text color indicator, a bulleted list with sample items, and the "Paper" canvas toggle button showing the moon icon (🌙 Theme). The live expansion preview container below also renders with matching white paper styling. -->

When **Rich Text** mode is selected, the visual formatting ribbon appears:

| Ribbon Control | Shortcut | Purpose |
| :--- | :--- | :--- |
| **Font Size Dropdown** | — | Choose from curated sizes: `9 pt`, `10 pt`, `11 pt` (default), `12 pt`, `14 pt`, `16 pt`, `18 pt`. |
| **Bold** | <kbd>Ctrl</kbd> + <kbd>B</kbd> | Toggles bold weight. |
| **Italic** | <kbd>Ctrl</kbd> + <kbd>I</kbd> | Toggles italic style. |
| **Underline** | <kbd>Ctrl</kbd> + <kbd>U</kbd> | Toggles text underline. |
| **Strikethrough** | — | Toggles horizontal strikethrough line. |
| **Text Color (`A`)** | — | Opens the Color Picker Flyout to set custom font colors, HEX codes, or choose "Automatic". |
| **Highlight Color** | — | Opens the Color Picker Flyout to apply yellow, green, cyan, or custom background highlights. |
| **Bulleted List** | — | Toggles bulleted point list (`•`). |
| **Numbered List** | — | Toggles sequential numbered list (`1.`, `2.`, `3.`). |
| **Alignment** | — | Align Left, Align Center, or Align Right. |
| **Clear Formatting** | <kbd>Ctrl</kbd> + <kbd>\</kbd> | Resets font, size, and styling. Unwraps lists into regular paragraphs and resets alignment to left. |
| **Canvas Mode Toggle** | — | Switches editor between **Paper Canvas** (default white sheet) and **Theme Canvas** (dark/light app background). |

#### The Color Picker Flyout
Clicking the Text Color or Highlight button summons a high-fidelity popup:
- **Quick Palette**: Curated primary colors with harmonious shades (Red, Green, Blue, Purple, Orange, Amber, Slate).
- **Custom Color Swatches**: Fine-tune custom colors using interactive RGB/HSV sliders or direct HEX code input (`#2563EB`).
- **Automatic Button**: Clears local color overrides so text dynamically inherits the destination application's ambient font color.

#### Paper Canvas vs. Theme Canvas View
By default, the Rich Text Editor and Live Expansion Preview render on a **Paper Canvas** (`#FFFFFF` pure white sheet with `#1E293B` dark text). This provides a 100% true WYSIWYG preview of how your formatted snippet will look when pasted into emails, Google Docs, Word documents, or tickets.
- Click the **`🌙 Theme`** button on the ribbon at any time to switch the canvas to match your current TriggerPoint theme (e.g., dark mode).
- *Crucial*: Switching canvas mode is purely a visual editor aid; it **never modifies or dirties** your snippet data.

#### Mode-Agnostic Normalization Engine
When you copy or paste rich text snippets, TriggerPoint uses a mode-agnostic export engine:
- Default, unstyled text is exported as `\cf0` in RTF and clean color-neutral HTML.
- When pasted into dark-mode apps (e.g., Slack dark mode, VS Code), it displays as light text. When pasted into light-mode apps (e.g., Microsoft Word, Outlook, Gmail), it displays as dark text.
- Any intentional custom styling you applied (e.g., Crimson Red text, Yellow highlight) is strictly preserved across all applications.

---

## 3. Dynamic Tokens & Placeholder Syntax

Both Plain Text and Rich Text snippets support dynamic tokens. When the snippet is triggered, TriggerPoint replaces tokens before sending the output into the destination app:

### Date & Time Tokens
| Token Syntax | Output Example | Notes |
| :--- | :--- | :--- |
| `{date}` | `2026-09-18` | Default standard ISO date format (`yyyy-MM-dd`). |
| `{date:format}` | `Friday, September 18, 2026` | Custom format using .NET date strings (e.g., `{date:dddd, MMMM d, yyyy}`). |
| `{date:+1d}` | `2026-09-19` | Relative date offset (+1 day). |
| `{date:-1w}` | `2026-09-11` | Relative date offset (-1 week). |
| `{tomorrow}` | `2026-09-19` | Shortcut alias for tomorrow. |
| `{yesterday}` | `2026-09-17` | Shortcut alias for yesterday. |
| `{time}` | `11:45 AM` | Default 12-hour time. |
| `{time:HH:mm:ss}` | `11:45:22` | Custom 24-hour time with seconds. |
| `{time:+2h}` | `01:45 PM` | Relative time offset (+2 hours). |
| `{datetime}` | `2026-09-18T11:45:00` | Full ISO timestamp. |

### System & Developer Tokens
| Token Syntax | Output Example | Notes |
| :--- | :--- | :--- |
| `{guid}` or `{uuid}` | `4f8e21a0-9c12-4d11-bf32-9012384aef01` | Generates a new cryptographically random GUID. |
| `{guid:upper}` | `4F8E21A0-9C12-4D11-BF32-9012384AEF01` | Uppercase GUID. |
| `{guid:N}` | `4f8e21a09c124d11bf329012384aef01` | 32 digits without hyphens. |
| `{guid:B}` | `{4f8e21a0-9c12-4d11-bf32-9012384aef01}` | Enclosed in braces. |
| `{username}` or `{user}` | `KevinSommers` | Current logged-in Windows username. |
| `{machine}` or `{computer}` | `WORKSTATION-01` | Current Windows computer hostname. |
| `{env:VAR_NAME}` | `C:\Users\KevinSommers` | Reads any Windows environment variable (e.g., `{env:USERPROFILE}`). |
| `{random:100,999}` | `482` | Random integer between specified min and max. |
| `{random:Alice,Bob,Charlie}` | `Bob` | Random item chosen from comma-separated choices. |

### Context, Clipboard & Caret Tokens
| Token Syntax | Description |
| :--- | :--- |
| `{active_window}` | Inserts the title of the active window immediately prior to snippet expansion. |
| `{active_process}` | Inserts the executable name of the active foreground application (e.g., `code.exe`). |
| `{clipboard}` | Injects the current text contents of the Windows clipboard. |
| `{clipboard:trim}` | Injects clipboard text with leading and trailing whitespace removed. |
| `{clipboard:upper}` | Injects clipboard text converted to uppercase. |
| `{clipboard:urlencode}` | Injects clipboard text with URL encoding applied. |
| `{cursor}` | Positions the text caret at this exact location after snippet expansion completes. |

---

### Interactive Prompt Tokens
Prompts pause snippet expansion to collect user input via a sleek modal dialog:

![Interactive Prompt Dialog](images/workflow-interactive-prompt-dialog.png)
<!-- SCREENSHOT REQUIRED: InteractivePromptDialog modal dialog displayed over the desktop with three fields: a single-line text input for "Customer Name", a dropdown choice selector for "Priority Level" (Low, Medium, High), and a date picker control for "Due Date", with "Confirm (Enter)" and "Cancel (Esc)" buttons. -->

- **Text Input**: `{text:Label|DefaultValue}`
  - Example: `{text:Client Name|Acme Corp}`
- **Multiline Text**: `{multiline:Label|DefaultValue}`
  - Example: `{multiline:Meeting Notes|No issues discussed.}`
- **Choice Dropdown**: `{choice:Label|Option1=Val1,Option2=Val2*}`
  - An asterisk (`*`) denotes the pre-selected default item.
  - Example: `{choice:Severity|Low=1,Medium=2*,High=3,Critical=4}`
- **Numeric Input**: `{number:Label|min,max|defaultValue}`
  - Example: `{number:Quantity|1,100|5}`
- **Date Picker**: `{date_picker:Label|DateFormat}`
  - Example: `{date_picker:Target Delivery Date|yyyy-MM-dd}`

---

## 4. Spotlight Command Palette & Quick Calculator

Summon the floating Spotlight Command Palette from any application by pressing <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Space</kbd> (or your custom shortcut).

![Spotlight Command Palette and Calculator](images/workflow-command-palette-calculator.png)
<!-- SCREENSHOT REQUIRED: Spotlight Command Palette floating in the center of the screen. The search bar has "=250 * 1.0825" typed into it. Directly below, an emerald-themed math calculation result card shows "Result: 270.625" with a small clipboard icon and hint "Press Enter to copy to clipboard". Below the card, recent search matches are listed. -->

### Features & Navigation
- **Instant Search**: Type any term to immediately filter across all actions and folder structures.
- **Prefix Filters**:
  - `> ` : Show only App / Shell actions.
  - `! ` : Show only Snippets.
  - `~ ` : Show only Workflows.
  - `# ` : Show only Folders.
- **Sort Modes**: Click the sort button or press <kbd>Ctrl</kbd> + <kbd>M</kbd> to toggle between:
  - `Smart Sort`: Balances recent usage and tree order.
  - `Frequency`: Sorts by most frequently executed actions.
  - `Alphabetical`: Standard A–Z order.
  - `Tree Order`: Strictly matches your sidebar folder hierarchy.
- **Keyboard Navigation**: Use <kbd>↑</kbd> and <kbd>↓</kbd> arrows to navigate; press <kbd>Enter</kbd> to execute; press <kbd>Esc</kbd> to close.

### Instant Math, Unit & Date Calculations
You can use the Command Palette as a high-speed calculator without opening a separate application:
1. Open the palette (<kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Space</kbd> or your configured hotkey).
2. Type an expression starting with `=` or a direct calculation formula:
   - Arithmetic: `= 45 * 1.15`, `(1200 - 350) / 4`
   - Percentages: `15% of 800`, `250 + 10%`
   - Powers & Roots: `2^8`, `sqrt(1024)`
   - Functions: `abs(-42)`, `sin(45)`, `log(100)`, `round(14.856, 2)`
   - Unit Conversions: `32px to rem`, `100 f to c`, `5 miles to km`, `16 gb to mb`, `50 usd to eur`
   - Date & Time Math: `now + 3d`, `now - 2w`, `time until Christmas`, `unix 1727184000`
3. The calculated result displays dynamically in the calculation banner beneath the search bar.
4. Press <kbd>Enter</kbd> to copy the result to your clipboard and close the palette, or press <kbd>Shift</kbd> + <kbd>Enter</kbd> to paste it directly into your foreground app.

---

### Quick Generators & Random Utilities 🎲

TriggerPoint includes a built-in cryptographic generator engine accessible right from the Command Palette search bar or the dedicated Calculator Workspace (`=`).

```
[ 🔄 ]  Result: 7f8c12a0-4b3e-7901-8e5f-1a2b3c4d5e6f  (UUIDv7)   [ ⚙ ]
```

#### 1. Cryptographic GUID & UUID Generator
Generate standard and modern RFC 9562 UUIDv7 identifiers:
- `guid` or `uuid` : Generates a standard hyphenated GUID (e.g., `d1b7f208-98e6-42d8-9fa4-762bb1e289c0`).
- `guid v7` : Generates a time-ordered UUIDv7 containing a millisecond timestamp prefix followed by cryptographic randomness.
- `guid upper` : Generates uppercase formatted GUID (`D1B7F208-98E6-42D8-9FA4-762BB1E289C0`).
- `guid nohyphen` or `guid n` : 32-character continuous hex string (`d1b7f20898e642d89fa4762bb1e289c0`).
- `guid braces` or `guid b` : Enclosed in braces (`{d1b7f208-98e6-42d8-9fa4-762bb1e289c0}`).
- `guid parens` or `guid p` : Enclosed in parentheses (`(d1b7f208-98e6-42d8-9fa4-762bb1e289c0)`).
- `guid b64` or `guid base64` : 22-character URL-safe Base64 encoded string (`0bfyCJjmQtifpHYrseKJwA`).

#### 2. Secure Password & PIN Generator
Generate cryptographically strong passwords using `RandomNumberGenerator`:
- `pass` or `password` : Generates a password using your default settings (default 16 characters with uppercase, lowercase, numbers, and symbols).
- `pass 24` : Generates a 24-character password.
- `pass 32 nosym` : Generates a 32-character alphanumeric password without symbols.
- `pass 16 nonum` : Generates a password without numbers.
- `pass 12 noupper` / `pass 12 nolower` : Controls case inclusion.
- `pin` or `pin 6` : Generates a secure numeric PIN of specified length (default: 4 digits, range 3–16).

#### 3. Diceware Passphrase Generator with 1337 / Leetspeak
Generate memorable, cryptographically secure multi-word passphrases drawn from a curated 2,048-word dictionary:
- `phrase` or `passphrase` : Generates a 4-word passphrase separated by hyphens (e.g., `walnut-beacon-velvet-crater`).
- `phrase 6` : Generates a 6-word passphrase (range: 2 to 12 words).
- `phrase 4 _` or `phrase 4 .` : Sets custom separator character (`_`, `.`, space, or `-`).
- `phrase nonum` : Excludes the trailing numeric suffix.
- `phrase nocap` : Keeps all words lowercase without Title Case capitalization.

##### 1337 (Leetspeak) & Phonetic Substitutions
Harden passphrases while keeping them memorable:
- `phrase leet` or `phrase 1337` : Applies visual character substitutions (e.g., `A` $\rightarrow$ `4`, `E` $\rightarrow$ `3`, `S` $\rightarrow$ `5`, `T` $\rightarrow$ `7`, `O` $\rightarrow$ `0`, `B` $\rightarrow$ `8`).
- `phrase phonetic` : Replaces phonetic sounds and syllables (e.g., `before` $\rightarrow$ `b4`, `great` $\rightarrow$ `gr8`, `to`/`too` $\rightarrow$ `2`, `for` $\rightarrow$ `4`).
- `phrase combo` : Combines both visual and phonetic substitutions.
- **Substitution Intensity**: Control substitution density via keywords:
  - `light` : ~25% substitution chance; replaces 1–2 characters per word.
  - `mod` or `moderate` : ~55% substitution chance (balanced).
  - `agg` or `aggressive` : ~90% substitution chance (maximum hardening).
  - *Example*: `phrase 4 leet agg` $\rightarrow$ `w4lnu7-b34c0n-v3lv37-cr473r9`.

#### 4. Tabletop Dice Roller, Random Numbers & Coin Flip
- `roll 2d20` : Rolls two 20-sided dice and outputs the sum with individual roll breakdown (e.g., `Roll: 29  (17 + 12)`).
- `roll 3d6+4` or `roll 1d100-5` : Supports positive and negative roll modifiers.
- `rand 1 100` : Generates a random integer within inclusive bounds.
- `coin` or `flip` : Simulates an unbiased cryptographic coin toss (`Heads` or `Tails`).

#### 5. Cryptographic Hashes & Text Encodings
- `sha256 Hello World` : Computes SHA-256 hash digest.
- `md5 Hello World` : Computes MD5 checksum.
- `sha512 secret` / `sha1 data` / `crc32 filedata` : Computes SHA-512, SHA-1, or CRC-32.
- `base64 Hello World` : Encodes text to standard Base64.
- `base64d SGVsbG8gV29ybGQ=` : Decodes Base64 to plaintext.
- `urlencode Query String` : Percent-encodes URI characters (`Query+String`).
- `urldecode Query+String` : Decodes percent-encoded URL text.
- `slug Hello World 2026!` : Converts text to SEO URL slug (`hello-world-2026`).

#### 6. Lorem Ipsum & Mock Data
- `lorem 3p` : Generates 3 paragraphs of standard Latin placeholder text.
- `lorem 50w` : Generates 50 words of placeholder text.
- `mock email` : Generates a realistic mock email address (e.g., `alex.chen@example.com`).
- `mock name` : Generates a realistic mock full name.
- `mock ip` : Generates a valid IPv4 address.
- `mock mac` : Generates a colon-delimited MAC address.

---

### In-Place Regeneration & Candidate Batch Drawer 🔄

When generating passwords, GUIDs, passphrases, or dice rolls, you often want to sample several choices or roll again:

1. **Instant In-Place Regeneration**:
   - Click the **`🔄` (Regenerate)** button on the calculation result card.
   - Or press <kbd>Tab</kbd>, <kbd>F5</kbd>, or <kbd>Ctrl</kbd> + <kbd>R</kbd> while the search box is focused.
   - The primary result immediately refreshes with a newly generated random value in-place without needing to retype your query.

2. **Candidate Batch Drawer**:
   - Click the **Alternative Choices / Units Drawer** toggle icon (`📐`) or press <kbd>Alt</kbd> + <kbd>U</kbd>.
   - TriggerPoint displays a batch of candidate values (configurable from 1 to 25 items in Settings; default: 10 candidates).
   - Click any candidate item to copy it directly, or click the **`[🔄 New Batch]`** button in the drawer header to regenerate the entire candidate list simultaneously.

3. **Jump to Generator Settings (`⚙`)**:
   - Click the gear icon on the result banner to open **Application Settings** directly focused on the **Generators** category.
   - Configure your personal defaults for password length, character sets, passphrase word count, separator style, 1337 substitution rules, GUID formatting, and batch sizes.

---

## 5. Shortcut Cheat Sheet HUD (<kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>/</kbd>)

Forget which shortcut does what? The Shortcut Cheat Sheet HUD provides an instant visual reference of every registered trigger:

![Shortcut Cheat Sheet HUD](images/workflow-cheat-sheet-hud.png)
<!-- SCREENSHOT REQUIRED: Shortcut Cheat Sheet HUD floating in the center of the screen with a semi-transparent dark background, rounded corners, and soft drop shadow. Two responsive columns display actions grouped under "General Tools" and "Developer Tools", with prominent hotkey badges ("Ctrl + Alt + T", "Win + N", "Alt + Space"). The top search box has "git" typed into it, filtering down to Git-related shortcuts. -->

- **Invoke**: Press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>/</kbd> from anywhere.
- **Live Search**: Type into the top search bar to filter shortcuts instantly.
- **Direct Execution**: Click any shortcut or press its hotkey to trigger it and automatically dismiss the HUD.
- **Dismiss**: Press <kbd>Esc</kbd> or click outside the window.

---

## 6. Floating Cursor Menus & Folder Launchers

Group multiple related actions into a folder and launch them directly under your mouse cursor with a single hotkey:

![Floating Cursor Popup Menu](images/workflow-cursor-popup-menu.png)
<!-- SCREENSHOT REQUIRED: Floating cursor menu anchored directly under the mouse pointer. The menu displays folder items with sequential quick-keys: "[1] Visual Studio", "[2] Terminal", "[3] Database Browser", "[A] Docker Desktop", and "[B] Edit Hosts". Pressing '1' or clicking launches Visual Studio. -->

### Setting Up a Cursor Menu
1. Click **+ Add Action** > **Folder**.
2. Name the folder (e.g., `Dev Launchers`) and assign a global hotkey (e.g., <kbd>Win</kbd> + <kbd>D</kbd>).
3. Set **Presentation Mode** to `Cursor Menu (Popup at mouse)`.
4. In the sidebar tree, drag your desired actions inside this folder.
5. In the folder editor, set **Auto-Number Mode**:
   - `Smart Fill`: Automatically assigns sequential quick-keys (`1–9`, `A–Z`) to actions that don't have an explicit accelerator key.
   - `Strict Positional`: Automatically numbers all items based on their exact physical position in the list.
6. Now, whenever you press <kbd>Win</kbd> + <kbd>D</kbd>, a sleek menu pops up at your cursor. Press `1`, `2`, `3`, or use arrow keys and <kbd>Enter</kbd> to trigger the item instantly.

---

## 7. Process & Context Filter Scoping

TriggerPoint allows you to restrict any shortcut, snippet, or entire folder to run **only** inside specific programs, or exclude certain applications:

![Process and Context Filter Tag Section](images/workflow-context-filter-tags.png)
<!-- SCREENSHOT REQUIRED: Process and Context Filter drawer expanded in the editor. "Allowed Processes" shows two tag pills: "code.exe" and "windowsterminal.exe". "Excluded Processes" shows "slack.exe". Below, the Browser URL Filter section is expanded with an allowed URL tag: "*github.com/*". -->

### Configuring Filters
1. Select any action or folder and scroll to the **Process Context Rules** drawer.
2. **Allowed Processes**:
   - Enter executable names (e.g., `notepad.exe`, `code.exe`, `chrome.exe`) and press <kbd>Enter</kbd>.
   - When configured, the shortcut will **only** fire when one of these applications is actively focused.
3. **Excluded Processes**:
   - Enter executables where the shortcut should be silenced (e.g., full-screen games, virtualization clients, remote desktop).
4. **Browser URL Filtering**:
   - When targeting web browsers (Chrome, Edge, Firefox, Brave), TriggerPoint inspects the active browser tab title and URL bar.
   - Add URL patterns with wildcards (e.g., `*github.com*`, `*atlassian.net*`). The shortcut will only trigger when the active browser tab matches.
5. **Window Target Crosshair Tool**:
   - Click and drag the crosshair icon (`🎯`) from TriggerPoint onto any open window on your desktop.
   - Release the mouse to automatically extract and populate the process executable name and browser URL pattern.
