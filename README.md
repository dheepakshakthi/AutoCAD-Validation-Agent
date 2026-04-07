# AutoCAD Validation Agent Plugin (CADY CoPilot)

## Project in Development...

## Description
In current product development workflows, design engineers rely extensively on manual verification to ensure compliance with established design standards. This process is time-consuming, prone to human error, and highly dependent on individual expertise. As a result, design gaps and non-compliance issues are often identified late in the development cycle, leading to rework, project delays, and reduced overall efficiency. <br>
There is a need for an intelligent system that supports engineers during the design phase by automatically validating designs against standard guidelines and performing real-time checks within the CAD environment. Such an AI-assisted solution should enable early detection of design issues, improve overall design quality, reduce iteration cycles, and ensure accurate product development.

## Scope
Design and develop a comprehensive software/program architecture to achieve the following objectives:
- **AI-Integrated CAD Assistance:** Development of a module that integrates with CAD tools to analyze designs in real-time and provide actionable insights.
- **Automated Design Validation:** The system should automatically detect deviations, inconsistencies, and potential risks, highlight them within the design, and generate detailed validation reports.

---

## ?? Features Added

- **CADY CoPilot UI**: A sleek, dockable Windows Presentation Foundation (WPF) palette integrated directly into AutoCAD that mirrors the look and feel of GitHub Copilot.
- **Groq AI Chat Integration**: Powered by the lightning-fast `qwen/qwen3-32b` model via Groq's REST API. It supports Markdown rendering and auto-hides complex "Thinking..." text cleanly under an expandable folder.
- **Design Validation Mode**: Chat with the AI to ask questions, pull up Standard Operating Procedures (SOP), and validate design constraints.
- **Agent Mode (Experimental)**: Equips the AI with an understanding of AutoCAD's model logic. Placed in Agent mode, the CoPilot can reason about your layout and emit structured JSON instructions (Actions) to programmatically alter the 3D model properties directly inside the CAD viewport.
- **Keep Attributes Horizontal**: Pre-existing feature (`KeepStraight` command) that adds an overrule, forcing Block Attributes to dynamically remain parallel to the X-axis regardless of block orientation.

## ?? How to Setup in Visual Studio 2022

1. **Prerequisites**
   - Visual Studio 2022 (with the **.NET Desktop Development** workload installed).
   - AutoCAD installed on your machine.
   - **.NET 8 SDK** (The project targets `.NET 8`).
   - Active internet connection for the API calls.

2. **Clone the Repository**
   ```bash
   git clone https://github.com/dheepakshakthi/AutoCAD-Validation-Agent.git
   ```

3. **Configure Project References in VS2022**
   - Open the `.sln` or `.csproj` solution in Visual Studio 2022.
   - Ensure the AutoCAD Managed API references (`AcCoreMgd.dll`, `AcDbMgd.dll`, `AcMgd.dll`) are correctly pointing to your local AutoCAD installation directory (e.g., `C:\Program Files\Autodesk\AutoCAD 20xx\`).
   - Verify that **Copy Local** is set to `False` for these AutoCAD reference DLLs.

4. **Build the Solution**
   - Build the solution (`Ctrl + Shift + B`). The project now compiles into `KeepAttributesHorizontal/artifacts/build/...` and also stages a fresh `NETLOAD` copy into `KeepAttributesHorizontal/artifacts/netload/...`.
   - After each successful build, the latest staged plugin path is written to `KeepAttributesHorizontal/artifacts/netload/x64/Debug/latest.txt`.

5. **Configure Backend Environment (Optional for Groq)**
   - Create `backend/.env` (you can copy `backend/.env.example`).
   - Add your Groq key:
     - `GROQ_API_KEY=<your_key>`
   - Optional model override:
     - `GROQ_MODEL=qwen/qwen3-32b`

6. **Run Backend Service**
   - From the `backend` folder:
   - `pip install -r requirements.txt`
   - `uvicorn main:app --host 127.0.0.1 --port 8000 --reload`
   - Check health: open `http://127.0.0.1:8000/health` and verify `groq_enabled`.

## ??? How to Use the Plugin in AutoCAD

1. **Load the Plugin (`NETLOAD`)**
   - Open AutoCAD.
   - Type the command `NETLOAD` in the AutoCAD command prompt and press Enter.
   - Open the path recorded in `KeepAttributesHorizontal/artifacts/netload/x64/Debug/latest.txt`, then select that staged `KeepAttributesHorizontal.dll`.
   - Using the staged `netload` copy prevents AutoCAD from locking the compiler's main output folder on the next build.

2. **Launch the Unified Workspace**
   - In the AutoCAD command prompt, type `ShowCadyWorkspace` and press Enter.
   - The **CADY Unified Agent Workspace** dockable palette appears as a single surface (no tabs).
   - When a drawing is opened for the first time, CADY prompts for project-specific validation constraints.
   - You can choose custom thresholds (radius, line length, text height, arc angle, blocked layers) or accept defaults.
   - Validation runs automatically whenever geometry changes; no separate validate command is required.
   - `ShowAiAssistant` and `ShowGuardrailPanel` remain valid aliases and now open the same unified workspace.
   - Ask questions in the input box and the unified agent will refresh guardrail status, recommendations, issue details, reports, and approvals through typed tool calls.

3. **Run Legacy Tools**
   - In the AutoCAD command prompt, type `KeepStraight` and press Enter.
   - This prevents text upside-down scenarios, enabling an overrule that forces all Block Attributes to stay horizontally readable.
