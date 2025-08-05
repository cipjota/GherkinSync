# GherkinSync

**GherkinSync** is a Visual Studio 2022 extension that enables seamless synchronization between Reqnroll Gherkin `.feature` files and Azure DevOps test cases. It automates the creation and update of test cases in Azure DevOps from your Gherkin scenarios directly within the IDE.

---

## ✨ Features

- 🔄 **Sync `.feature` Scenarios to Azure DevOps**: Create or update Azure DevOps test cases based on Reqnroll Gherkin scenarios.
- 🧪 **Test Plan and Suite Association**: Link Gherkin scenarios with specific Azure DevOps test plans and suites using tags.
- ⚙️ **Custom Field Support**: Assign default values to custom fields in test cases.
- 💡 **Automation Integration**: Automatically associate automated test methods with test cases.
- 🧼 **Background Step Inclusion**: Optionally include `Background` steps as part of each scenario.
- 🗑️ **Remove Obsolete Test Cases**: Optionally remove test cases from the test suite that are no longer in the `.feature` file.

---

## 📦 Installation

1. Open **Visual Studio 2022**.
2. Go to **Extensions > Manage Extensions**.
3. Search for `GherkinSync`.
4. Click **Download** and restart Visual Studio to install.

> Alternatively, build the project from source using the included `.sln` file.

---

## 🚀 Usage

1. Open a `.feature` file in Visual Studio.
2. Right-click inside the file or use the command palette to run **GherkinSync**.
3. Fill in the required **Sync Options**:
   - Azure DevOps project name
   - PAT Token (Personal Access Token)
   - Test Plan and Suite IDs
   - Description template
   - Custom field mappings
4. Click **Sync** to begin synchronization.

> The tool will:
> - Parse scenarios
> - Extract steps and examples
> - Create/update corresponding test cases in Azure DevOps
> - Embed `@TestCaseId(...)`, `@TestSuiteId(...)`, and `@TestPlanId(...)` tags in the `.feature` file

---

## 🏷️ Gherkin Tags

Use the following tags to link `.feature` files to Azure DevOps entities:

- `@TestPlanId(1234)` — associates the feature with a Test Plan.
- `@TestSuiteId(5678)` — associates the feature with a Test Suite.
- `@TestCaseId(101,102)` — assigns specific Test Case IDs to a scenario.

These tags will be automatically updated after each sync.

---

## 🛠️ Configuration

Access the plugin settings via:

**Tools > Options > GherkinSync > Settings**

Configuration options include:

- Azure DevOps Base URL
- Personal Access Token (PAT)
- Project Name
- Test case description template
- Custom field definitions
- Sync behavior options (e.g., remove obsolete test cases, include background steps)

---

## 🔐 License

This project is licensed under the **European Union Public Licence (EUPL) v1.2**. See the [LICENSE](./LICENSE) file for details.

---

## 🤝 Contributions

Contributions, feature requests, and bug reports are welcome! Please open an issue or submit a pull request.

---

## 📄 Requirements

- Visual Studio 2022
- .NET Framework 4.8
- Azure DevOps PAT with appropriate permissions
- Reqnroll `.feature` files with valid Gherkin syntax

