# TriggerPoint Workflows & Automation 🔀

This guide covers advanced automation capabilities in TriggerPoint: building visual multi-step sequences, conditional branching (`IfCondition`), drag-and-drop step organization, the hardware-backed Windows DPAPI Secrets Vault, JavaScript scripting with the Jint engine, and interactive workflow debugging.

---

## 1. Visual Step Builder Overview

The Visual Step Builder allows you to assemble complex automation sequences by stacking pre-built, configurable step cards—without writing any code.

![Visual Workflow Step Builder](images/workflow-visual-step-builder.png)
<!-- SCREENSHOT REQUIRED: Workflow action details panel showing a chained sequence of visual step cards: (1) Prompt User step collecting "Ticket ID", (2) IfCondition step checking if ticket starts with "BUG", (3) Launch App step inside THEN branch launching browser to Jira, (4) Delay step (500ms), and (5) Inject Snippet step pasting a standardized response. -->

### Supported Workflow Step Types

| Step Type | Icon | Purpose & Capabilities |
| :--- | :---: | :--- |
| **Prompt User** | `💬` | Collects runtime user inputs via textboxes, multiline inputs, numbers, dropdown choices, or date pickers. Stores inputs in workflow variables. |
| **If Condition** | `⚖` | Evaluates a logical expression and routes execution through **THEN** (True) and optional **ELSE** (False) branches. |
| **Open URL** | `🌐` | Opens a website in the default or specified browser (Chrome, Edge, Firefox, Brave) with optional private/incognito mode. |
| **Ensure Directory** | `📁` | Verifies folder existence on disk. Options: silently create if missing, prompt user to create, or throw an error. |
| **Launch App** | `⚡` | Executes an executable or command with custom arguments, working directory, Run as Admin, and monitor display targeting. |
| **Inject Snippet** | `📝` | Sends simulated keystrokes or pastes rich/plain text templates containing dynamic tokens into the active window. |
| **Delay** | `⏱` | Pauses execution for a specified duration in milliseconds (e.g., waiting for an app window to initialize). |
| **Execute Action** | `▶` | Calls another TriggerPoint action, runs a macro, or opens a nested folder cursor menu. |
| **Inline Script** | `📜` | Executes a targeted snippet of JavaScript directly within the visual sequence. |

---

## 2. Conditional Branching (`IfCondition`)

Conditional branching enables your workflows to make intelligent decisions at runtime based on user input, variables, file system state, or running processes:

![Conditional Branching Step Card](images/workflow-conditional-branching.png)
<!-- SCREENSHOT REQUIRED: Expanded IfCondition step card showing Left Operand "{{ENVIRONMENT}}", Operator dropdown selected to "Equals (=)", Right Operand "Production", and Case Insensitive switch enabled. Beneath, the green THEN rail contains an Ensure Directory step, and the amber ELSE rail contains a Launch App step. The "▶ Test Condition Live" button is highlighted. -->

### Available Comparison Operators

#### String & Pattern Operators
- **Equals (`=`)** / **Does Not Equal (`≠`)**: Full string matching.
- **Contains** / **Does Not Contain**: Substring search.
- **Starts With** / **Ends With**: Prefix and suffix checking.
- **Matches Regex**: Evaluates regular expressions (e.g., `^[A-Z]{3}-\d+$`).
- **Is Empty** / **Is Not Empty**: Checks for null or whitespace values.

#### Numeric Operators
- **Greater Than (`>`)** / **Greater or Equal (`≥`)**: Compares numbers.
- **Less Than (`<`)** / **Less or Equal (`≤`)**: Compares numbers.

#### System & Environmental Operators
- **File Exists**: Evaluates to true if the path in the left operand exists as a file on disk.
- **Directory Exists**: Evaluates to true if the folder path exists on disk.
- **Process is Running**: Checks whether an executable (e.g., `docker.exe`, `vpn.exe`) is actively running in Windows Task Manager.

### Case-Sensitivity & Live Testing
- **Case-Insensitive Match**: Toggle to ignore casing differences (e.g., matching `prod` with `PROD`).
- **▶ Test Condition Live**: Click the test button on any condition card to immediately evaluate the expression against current workflow variables and system state in an interactive test modal.

---

## 3. Drag-and-Drop Step Reordering & Nesting

Reordering steps and moving actions in and out of conditional branches is handled through an ergonomic, zero-flicker drag-and-drop system:

![Workflow Drag and Drop Targeting](images/workflow-drag-drop-targeting.png)
<!-- SCREENSHOT REQUIRED: Drag-and-drop action in progress inside the workflow builder. A step card is being dragged, displaying a directional ghost badge ("↓ Drop after Step 2"). A high-contrast cyan insertion drop line with a starting bead ("●───────") indicates the exact insertion point between two cards. -->

### Drag-and-Drop Mechanics
- **Card Drag Handle (`⠿`)**: Click and drag from any step card header.
- **Precision Drop Line (`●───────`)**: A crisp, themed drop line with an anchor bead shows the exact target position (above or below any card).
- **Branch Nesting**: Drag a step directly over the `THEN` or `ELSE` rail of an `IfCondition` card to nest it inside that execution branch.
- **Directional Ghost Badge**: A floating helper pill follows your mouse cursor displaying contextual feedback (e.g., `↑ Drop before Step 1` or `↪ Move into THEN branch`).
- **Edge Auto-Scrolling**: Dragging within 50px of the top or bottom viewport automatically scrolls the editor smoothly.

### Step Context Menu (`⋮`)
Click the kebab menu button on any step card for one-click operations:
- **Move to THEN Branch** / **Move to ELSE Branch**: Instantly nests the step without dragging.
- **Move out of Branch**: Promotes the step back to the root workflow sequence.
- **Cut / Copy / Paste**: Transfer steps between workflows.
- **Duplicate**: Clones the step card with all configured parameters.
- **Delete**: Removes the step.

---

## 4. Workflow Variables & Hardware-Backed Secrets Vault

Workflows can declare reusable variables that pass dynamic data between steps or scripts:

![Workflow Variables Editor and Secrets Vault](images/workflow-secrets-vault-entry.png)
<!-- SCREENSHOT REQUIRED: Workflow Variables configuration drawer showing three variables: (1) "PROJECT_DIR" (public string, "C:\Source\App"), (2) "TARGET_ENV" (public string, "Staging"), and (3) "API_KEY" with the Secret padlock badge enabled and masked value "••••••••••••" (encrypted with Windows DPAPI at rest as "vault:dpapi:..."). -->

### Hardware-Backed Secrets Vault (Windows DPAPI)
Sensitive values such as API tokens, database passwords, and personal access tokens should never be stored in plaintext. TriggerPoint includes a built-in **Secrets Vault** powered by the **Windows Data Protection API (DPAPI)**:
1. When adding a variable, toggle the **Secret** checkbox.
2. The value is immediately encrypted using DPAPI tied to your Windows user account and machine hardware key.
3. In the serialized configuration file (`triggers.json`), the value is stored as an encrypted ciphertext envelope: `vault:dpapi:AQAAANCMnd8BFdERjHoAwE...`
4. The plaintext value is decrypted strictly in-memory during workflow execution and never leaked into logs or export files.

### Variable Interpolation Syntax
Reference workflow variables anywhere in text boxes, arguments, URLs, or snippets using double braces:
```text
https://api.github.com/repos/{{GITHUB_ORG}}/{{REPO_NAME}}
```

### Searchable Variable Picker Dialog
When editing any text field in a workflow step, click the **{x} Variable** button to open the Variable Picker:

![Searchable Variable Picker Dialog](images/workflow-variable-picker-dialog.png)
<!-- SCREENSHOT REQUIRED: VariablePickerDialog modal dialog with real-time search bar at the top filtered to "path". Filter category chips show All, System, Workflow, Clipboard, and Date. Below, a list of matching tokens and variables is displayed with descriptions and a "Insert Variable" button. -->

- **Categories**: Filter by `All`, `System`, `Workflow`, `Clipboard`, or `Date`.
- **Search**: Fuzzy search across variable names and built-in tokens.
- **Live Preview**: Inspect current values before inserting them into your step.

---

## 5. JavaScript Scripting Engine (Jint)

For maximum flexibility, TriggerPoint embeds **Jint**, a full ECMA-compliant JavaScript runtime that executes natively inside the .NET process with microsecond latency.

![AvalonEdit JavaScript Script Editor](images/workflow-javascript-avalon-editor.png)
<!-- SCREENSHOT REQUIRED: Full JavaScript Workflow Editor mode featuring the dual AvalonEdit code editor with JavaScript syntax highlighting, line numbers, and dark theme styling. The code editor displays a script using "tp.prompt()", "tp.launch()", and "tp.vars.get()", with the "Visual Mode" return button visible at the top. -->

### Switching between Visual and Script Modes
At the top of the workflow editor, toggle between **Visual Steps** and **JavaScript Script**:
- **Convert Visual to Script**: Automatically compiles your visual step sequence into clean, readable JavaScript.
- **Return to Visual**: Recompiles JavaScript back into visual step cards where applicable, with a safety prompt to prevent accidental data loss.

### Native `tp` Runtime API Reference
Inside scripts, the global `tp` object provides access to TriggerPoint's capabilities:

```javascript
// 1. Interactive Prompts
const response = tp.prompt("Deployment Confirmation", [
    { name: "env", label: "Target Environment", type: "choice", options: ["Staging", "Production*"] },
    { name: "version", label: "Release Tag", type: "text", defaultValue: "v2.0.9" }
]);

if (!response) {
    tp.log("warn", "Workflow cancelled by user.");
    return;
}

// 2. Variables & Secrets
const apiKey = tp.vars.get("API_SECRET_KEY");
tp.vars.set("DEPLOY_STATUS", "In Progress");

// 3. File System Operations
const configPath = "C:\\Projects\\deploy.json";
if (tp.fs.fileExists(configPath)) {
    const rawConfig = tp.fs.readFile(configPath);
    tp.log("info", `Loaded config: ${rawConfig.length} bytes`);
}

// 4. Application Launch & Multi-Monitor
tp.launch("C:\\Tools\\deploy.exe", `--env ${response.env} --key ${apiKey}`, "C:\\Tools", true);

// 5. Delays & Snippet Injection
tp.delay(1000);
tp.injectSnippet(`Deployed version ${response.version} successfully at {time:hh:mm tt}!`);
```

---

## 6. Workflow Debug Dialog

Test and troubleshoot complex multi-step workflows before deploying them into daily use:

![Workflow Debug Dialog](images/workflow-debug-dialog.png)
<!-- SCREENSHOT REQUIRED: WorkflowDebugDialog modal window displaying an active workflow execution trace. The left pane shows the visual step execution pipeline with completed steps checked in green. The right pane shows the live Variable Inspection table with current values and the bottom Console Output log showing timestamped execution messages. -->

- **Launch**: Click **Test Action** (<kbd>F5</kbd>) or select **Debug Workflow** from the action menu.
- **Step-by-Step Execution**: Inspect each step as it runs, including condition evaluation results and branch choices.
- **Variable Inspector**: View runtime variable values, evaluated expressions, and modified states.
- **Execution Log**: Real-time timestamped log output capturing process exit codes, file system results, and script warnings.
