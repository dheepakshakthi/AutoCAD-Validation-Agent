# AutoCAD AI Agent Integration Strategy

This document outlines the conceptual architectural overview and strategy for integrating an AI agent into the AutoCAD .NET plugin. The goal is to evolve the current chat interface into an agent capable of modifying 2D and 3D designs.

To achieve this, an orchestration layer is required to act as the "brain" connecting the LLM's reasoning with AutoCAD's execution environment.

## 1. The Tool Definition Layer

Define the capabilities the agent should have. These are the "Tools."

*   **Concepts as Actions:** Each tool represents a specific AutoCAD operation, such as `DrawLine`, `GetSelectedEntities`, `ModifyAttributeValue`, or `MoveEntity`.
*   **Metadata (The Schema):** For the LLM to know a tool exists and how to use it, it must be defined using a JSON schema (standard for OpenAI/Groq function calling). This schema includes the tool's name, a clear description of what it does, and the exact parameters it requires (e.g., X, Y, Z coordinates for a point).
*   **Implementation (The Wrapper):** Behind the scenes, each tool is backed by a C# method that wraps the standard AutoCAD .NET API calls.

## 2. LLM Tool Calling (Function Calling)

Update the communication with the Qwen3 model to support tool calling.

*   **Prompting with Tools:** When sending the user's message to the LLM, also send the JSON schemas of all available tools in the request payload.
*   **Handling the Response:** Instead of just returning conversational text, the LLM will analyze the user's request. If it decides it needs to take an action, it will return a specialized response requesting to "call a tool," providing the name of the tool and the arguments it generated based on the user's intent.

## 3. The Orchestration Loop (ReAct Pattern)

This core loop manages the interaction between the user, the LLM, and AutoCAD using the **ReAct** (Reasoning and Acting) pattern.

1.  **Analyze:** The user asks a question (e.g., "Move the selected circle 10 units up"). The orchestrator sends the prompt, conversation history, and available tools to the LLM.
2.  **Act (Tool Call):** The LLM responds, asking to call a specific tool (e.g., `GetSelectedEntities`).
3.  **Execute & Return:** The orchestrator intercepts this request, runs the corresponding C# method inside AutoCAD, gets the result (e.g., "Found 1 Circle at 0,0,0"), and appends this result to the conversation history as a "Tool Response".
4.  **Repeat:** The orchestrator immediately sends this updated history back to the LLM. The LLM might then request to call another tool (e.g., `MoveEntity` with new coordinates).
5.  **Finalize:** Once the LLM has completed the task, it responds with a standard text message ("I have moved the circle 10 units up"), which the orchestrator finally displays in the WPF UI.

## 4. Safe AutoCAD API Interaction (Crucial)

This is the most critical part of an AutoCAD integration. The UI (and async network calls to the LLM) runs on a different context than the AutoCAD engine. The agent *cannot* arbitrarily modify the drawing whenever it wants.

*   **Execution Context:** When the orchestrator receives a tool call that modifies the drawing, that execution *must* be marshaled back to the main AutoCAD thread. Use `Application.DocumentManager.MdiActiveDocument.ExecuteInCommandContext` or `ExecuteInApplicationContext` to ensure the tool runs safely.
*   **Document Locking:** Before any tool modifies the database, it must obtain a document lock (`using (doc.LockDocument())`).
*   **Transactions:** Every database modification (adding, editing, deleting entities) must be wrapped in an AutoCAD `Transaction`.
*   **Graceful Failure:** If an AutoCAD API call fails (e.g., the user didn't have anything selected), the tool must catch the exception and return a friendly error message *back to the LLM* (not a crash). The LLM can then read the error and try a different approach or ask the user for clarification.

## 5. Integration Point

In the current setup (`AiAssistantControl.xaml.cs`), the `GenerateAiResponseAsync` method handles a simple chat request. 

To build the agent:
1.  Abstract this logic into a dedicated `OrchestrationService`.
2.  The UI will simply pass the user's input to this service and wait.
3.  The service manages the ReAct loop in the background.
4.  The service delegates actual tool execution to a `CommandDispatcher` that handles the safe context-switching, locking, and transactions required by AutoCAD.