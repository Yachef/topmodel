import { LanguageClient, LanguageClientOptions, ServerOptions } from 'vscode-languageclient/node';
import { Trace } from 'vscode-jsonrpc';
import { ExtensionContext, workspace, commands, window, StatusBarItem, StatusBarAlignment, Terminal, Uri } from 'vscode';
import * as fs from "fs";
import { TopModelConfig } from './types';
const open = require('open');
const pjson = require('../package.json');

const exec = require('child_process').exec;
const yaml = require("js-yaml");

let NEXT_TERM_ID = 1;
let currentTerminal: Terminal;
let lsStarted = false;

export function activate(context: ExtensionContext) {
    if (!lsStarted) {
        createStatusBar();
        checkInstall();
        findConfFile().then((conf) => {
            const config = ((conf as any).config) as TopModelConfig;
            const configPath = (conf as any).file.path;
            startLanguageServer(context, configPath, config);
            registerCommands(context, configPath);
        }, error => {
            handleError(error);
        });
    }
}

let topModelStatusBar: StatusBarItem;


function createStatusBar() {
    topModelStatusBar = window.createStatusBarItem(StatusBarAlignment.Right, 100);
    topModelStatusBar.text = '$(loading) Topmodel';
    topModelStatusBar.tooltip = 'Topmodel is loading configuration';
    topModelStatusBar.show();
}

function execute(command: string, callback: Function) {
    exec(command, function (error: string, stdout: string, stderr: string) { callback(stdout); });
}
function checkInstall() {
    execute('echo ;%PATH%; | find /C /I "dotnet"', async (dotnetIsInstalled: string) => {
        if (dotnetIsInstalled !== '1\r\n') {
            const selection = await window.showInformationMessage('Dotnet is not installed', "Show download page");
            if (selection === "Show download page") {
                open("https://dotnet.microsoft.com/download/dotnet/6.0");
            }
        } else {
            checkTopModelInsall();
        }
    });
}

function checkTopModelInsall() {
    execute('dotnet tool list -g | find /C /I "topmodel"', async (result: string) => {
        if (result !== '1\r\n') {
            const option = "Install TopModel";
            const selection = await window.showInformationMessage('TopModel is not installed', option);
            if (selection === option) {
                const terminal = window.createTerminal("TopModel install");
                terminal.sendText("dotnet tool install --global TopModel.Generator");
                terminal.show();
            }
        } else {
            checkTopModelUpdate();
        }
    });
}

async function checkTopModelUpdate() {
    const https = require('https');
    const options = {
        hostname: 'api.nuget.org',
        port: 443,
        path: '/v3-flatcontainer/TopModel.Generator/index.json',
        method: 'GET'
    };

    const req = https.request(options, (res: any) => {
        res.on('data', (reponse: string) => {
            const { versions }: {versions: string[]} = JSON.parse(reponse);
            execute(`dotnet tool list -g | find /C /I "topmodel.generator      ${versions[versions.length - 1]}"`, async (result: string) => {
                if (result !== '1\r\n') {
                    const option = "Update TopModel";
                    const selection = await window.showInformationMessage('TopModel can be updated', option);
                    if (selection === option) {
                        const terminal = window.createTerminal("TopModel install");
                        terminal.sendText("dotnet tool update --global TopModel.Generator");
                        terminal.show();
                    }
                }
            });
        });
    });

    req.on('error', (error: any) => {
        console.error(error);
    });

    req.end();


}

function registerCommands(context: ExtensionContext, configPath: any) {
    context.subscriptions.push(commands.registerCommand(
        "extension.topmodel",
        () => {
            startModgen(false, configPath);
        }
    ));
    context.subscriptions.push(commands.registerCommand(
        "extension.topmodel.watch",
        () => {
            startModgen(true, configPath);

        }
    ));
    return NEXT_TERM_ID;
}

async function findConfFile(): Promise<{ config: TopModelConfig, file: Uri }> {
    const files = await workspace.findFiles("**/topmodel*.config");
    let configs: { config: TopModelConfig, file: Uri }[] = files.map((file) => {
        const doc = fs.readFileSync(file.path.substring(1), "utf8");
        const c = doc
            .split("---")
            .filter(e => e)
            .map(yaml.load)
            .map(e => e as TopModelConfig)
            .filter(e => e.app)
        [0];
        return { config: c, file };
    });
    if (configs.length > 1) {
        throw new TopModelException("Plusieurs fichiers de configuration trouvés. L'extension n'a pas démarré (coming soon)");
    } else if (configs.length === 0) {
        throw new TopModelException("Topmodel a démarré car un fichier de configuration se trouvait dans votre workspace, mais il est désormais introuvable.");
    }
    return configs[0];
}

function startLanguageServer(context: ExtensionContext, configPath: string, config: TopModelConfig) {
    // The server is implemented in node
    let serverExe = 'dotnet';

    const args = [context.asAbsolutePath("./language-server/TopModel.LanguageServer.dll")];
    let configRelativePath = workspace.asRelativePath(configPath);
    if ((workspace.workspaceFolders?.length || 0) > 1) {
        configRelativePath = configRelativePath.split("/").splice(1).join('/');
    }
    args.push(configPath.substring(1));
    let serverOptions: ServerOptions = {
        run: { command: serverExe, args },
        debug: { command: serverExe, args }
    };
    let configFolderA = configRelativePath.split("/");
    configFolderA.pop();
    const configFolder = configFolderA.join('/');
    let modelRoot = config.modelRoot || configFolder;
    // Options to control the language client
    let clientOptions: LanguageClientOptions = {
        // Register the server for plain text documents
        documentSelector: [{ language: 'yaml' }, { pattern: `${modelRoot}**/tmd` }],
        synchronize: {
            configurationSection: 'topmodel',
            fileEvents: workspace.createFileSystemWatcher(`${modelRoot}**/*.tmd`)
        },
    };

    // Create the language client and start the client.
    const client = new LanguageClient('topmodel', 'TopModel', serverOptions, clientOptions);
    client.trace = Trace.Verbose;
    let disposable = client.start();
    client.onReady().then(() => handleLsReady(config, context));

    // Push the disposable to the context's subscriptions so that the
    // client can be deactivated on extension deactivation
    context.subscriptions.push(disposable);
}
function startModgen(watch: boolean, configPath: string) {
    if (!currentTerminal || !window.terminals.includes(currentTerminal)) {
        currentTerminal = window.createTerminal({
            name: `Topmodel : #${NEXT_TERM_ID++}`,
            message: "Starting modgen in a new terminal"
        });
    }
    currentTerminal.show();
    currentTerminal.sendText(
        `modgen ${configPath}` + (watch ? " --watch" : "")
    );

}

function handleLsReady(config: TopModelConfig, context: ExtensionContext): void {
    topModelStatusBar.text = "$(check-all) TopModel";
    topModelStatusBar.tooltip = "TopModel is running for app " + config.app;
    topModelStatusBar.command = "extension.topmodel";
    context.subscriptions.push(topModelStatusBar);
    lsStarted = true;
}
class TopModelException {
    constructor(public readonly message: string) { }
}

function handleError(exception: TopModelException) {
    window.showErrorMessage(exception.message);
    topModelStatusBar.text = "$(diff-review-close) TopModel";
    topModelStatusBar.tooltip = "TopModel is not running";
}