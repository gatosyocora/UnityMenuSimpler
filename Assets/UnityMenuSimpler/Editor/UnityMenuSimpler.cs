using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Reflection;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Gatosyocora.UnityMenuSimpler.DataClass;

// UnityMenuSimpler v1.0
// Copyright (c) 2020 gatosyocora
// MIT License

namespace Gatosyocora.UnityMenuSimpler
{
    public class UnityMenuSimpler : EditorWindow
    {
        private List<EditorWindowInfo> editorWindowInfoList;
        private string folderName = string.Empty;
        private List<EditorWindowFolder> folderList;

        /// <summary>
        /// MenuItemのフォルダの除外対象
        /// </summary>
        private readonly static string[] exclusionFolderNames = new string[] { "GameObject", "CONTEXT", "Assets" };

        private Vector2 unallocatedListScrollPos = Vector2.zero;
        private Vector2 folderListScrollPos = Vector2.zero;
        private Rect folderRect;

        private readonly static string T00L_KEYWORD = "UNITYMENUSIMPLER:";

        [MenuItem("GatoTool/UnityMenuSimpler")]
        public static void Open()
        {
            GetWindow<UnityMenuSimpler>("UnityMenuSimpler");
        }

        private void OnEnable()
        {
            editorWindowInfoList = LoadEditorWindowList();
            folderList = CreateExistFolderList(editorWindowInfoList);
        }

        private void OnGUI()
        {
            if (editorWindowInfoList != null)
            {
                EditorGUILayout.Space();

                using (var scroll = new EditorGUILayout.ScrollViewScope(folderListScrollPos,
                                            alwaysShowVertical: false,
                                            alwaysShowHorizontal: true))
                using (var scope = new EditorGUILayout.HorizontalScope())
                {
                    folderListScrollPos = scroll.scrollPosition;
                    folderRect = scope.rect;

                    foreach (var folder in folderList.ToList())
                    {
                        if (folder.ParentFolder != null) continue;

                        using (var check = new EditorGUI.ChangeCheckScope())
                        {
                            if (GatoGUILayout.FolderField(folder))
                            {
                                // ファイルを移動させたときの処理
                                foreach (var selectedItem in editorWindowInfoList.Where(x => x.Selected))
                                {
                                    if (folder.EditorWindowList.Contains(selectedItem)) continue;

                                    selectedItem.Selected = false;
                                    var filePath = selectedItem.SourceMenuItemPath.Split('/').Last();
                                    selectedItem.DestMenuItemPath = folder.Name + "/" + filePath;
                                    folder.EditorWindowList.Add(selectedItem);
                                }

                                // フォルダを移動させたときの処理
                                foreach (var selectedFolder in folderList.Where(x => x.Selected))
                                {
                                    if (selectedFolder == folder ||
                                        folder.EditorWindowFolderList.Contains(selectedFolder)) continue;

                                    if (selectedFolder.ParentFolder != null)
                                    {
                                        selectedFolder.ParentFolder.EditorWindowFolderList.Remove(selectedFolder);
                                    }

                                    selectedFolder.Selected = false;
                                    folder.EditorWindowFolderList.Add(selectedFolder);
                                    selectedFolder.ParentFolder = folder;

                                    // フォルダに属するファイルへの処理
                                    foreach (var containItem in selectedFolder.EditorWindowList)
                                    {
                                        containItem.DestMenuItemPath = folder.Name + "/" + containItem.DestMenuItemPath;
                                    }
                                }
                            }

                            if (check.changed)
                            {
                                Repaint();
                            }
                        }
                    }

                    if (GatoGUILayout.DropArea("Drop SubFolder", EditorGUIUtility.singleLineHeight * 5f))
                    {
                        foreach (var folder in folderList.ToArray())
                        {
                            foreach (var selectedFolder in folder.EditorWindowFolderList.Where(x => x.Selected))
                            {
                                selectedFolder.Selected = false;

                                if (selectedFolder.ParentFolder == null) continue;

                                var parentFolder = selectedFolder.ParentFolder;
                                var parentFolderPath = GetMenuItemFolderPath(parentFolder);
                                parentFolder.EditorWindowFolderList.Remove(selectedFolder);
                                folderList.Add(selectedFolder);
                                selectedFolder.ParentFolder = null;

                                var folderPath = GetMenuItemFolderPath(selectedFolder);
                                foreach (var containItem in selectedFolder.EditorWindowList)
                                {
                                    containItem.DestMenuItemPath = folderPath + "/" + containItem.SourceMenuItemPath.Split('/').Last();
                                }
                            }
                        }
                    }
                }

                EditorGUILayout.Space();

                if (GUILayout.Button("Add Folder"))
                {
                    folderListScrollPos.x = folderRect.width;

                    var newFolder = new EditorWindowFolder()
                    {
                        NameEdittable = true
                    };
                    folderList.Add(newFolder);
                }

                EditorGUILayout.Space();

                EditorGUILayout.LabelField("Unallocated", EditorStyles.boldLabel);

                using (var scroll = new EditorGUILayout.ScrollViewScope(unallocatedListScrollPos))
                {
                    unallocatedListScrollPos = scroll.scrollPosition;

                    foreach (var editorWindowInfo in editorWindowInfoList)
                    {
                        if (!string.IsNullOrEmpty(editorWindowInfo.DestMenuItemPath)) continue;

                        using (var check = new EditorGUI.ChangeCheckScope())
                        {
                            editorWindowInfo.Selected = GatoGUILayout.ToggleLabelArea(
                                                            editorWindowInfo.Name,
                                                            editorWindowInfo.Selected,
                                                            Color.white, Color.grey);

                            if (check.changed) Repaint();
                        }
                    }
                }

                EditorGUILayout.Space();

                using (new EditorGUI.DisabledScope(!editorWindowInfoList.Any(x => x.HasChanged)))
                {
                    if (GUILayout.Button("Apply"))
                    {
                        ReplaceMenuItem(editorWindowInfoList);
                    }
                }
            }
        }

        /// <summary>
        /// 特定の型クラスのファイルのパスをAssetsフォルダ以下から取得する
        /// </summary>
        /// <param name="type">クラスの型</param>
        /// <returns>ファイルパス</returns>
        private string GetFilePath(Type type)
        {
            var assetGuid = AssetDatabase.FindAssets(type.Name + " t:Script").FirstOrDefault();
            if (string.IsNullOrEmpty(assetGuid))
            {
                Debug.LogError(type.Name + " : Not Found Asset");
                return string.Empty;
            }

            var path = AssetDatabase.GUIDToAssetPath(assetGuid);

            if (!File.Exists(path))
            {
                Debug.LogError(type.Name + " : Not Found File");
                Debug.LogError(path);
                return string.Empty;
            }

            return path;
        }

        /// <summary>
        /// 特定の型のクラスが特定のアトリビュートを持つメソッドを含んでいるか判定する
        /// </summary>
        /// <param name="type">クラスの型</param>
        /// <param name="attrType">アトリビュートの型</param>
        /// <returns>含まれる場合true</returns>
        private bool ContainAttribute(Type type, Type attrType)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .SelectMany(x => x.CustomAttributes)
                        .Any(x => x.AttributeType == attrType);
        }

        /// <summary>
        /// 特定の型のクラスの関数が持つアトリビュートMenuItemのパスを取得する
        /// </summary>
        /// <param name="type">MenuItemアトリビュートをつけた関数を持つクラスの型</param>
        /// <returns>MenuItemのパス</returns>
        private string GetMenuItemPath(Type type)
        {
            var attr = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .SelectMany(x => x.CustomAttributes)
                        .Where(x => x.AttributeType == typeof(MenuItem))
                        .Where(x => !ContainExclusionFolder(x, exclusionFolderNames))
                        .FirstOrDefault();

            if (attr == null)
            {
                return string.Empty;
            }

            return attr.ConstructorArguments.Select(x => x.Value as string).FirstOrDefault();
        }

        /// <summary>
        /// Assetsフォルダ以下からMenuItemアトリビュートをもつスクリプトの一覧を取得する
        /// </summary>
        /// <returns>MenuItemアトリビュートをもつスクリプトのリスト</returns>
        private List<EditorWindowInfo> LoadEditorWindowList()
        {
            return Assembly.GetExecutingAssembly()
                        .GetTypes()
                        .Where(x => ContainAttribute(x, typeof(MenuItem)))
                        .Select(x =>
                        {
                            var menuItemPath = GetMenuItemPath(x);

                            return new EditorWindowInfo()
                            {
                                Name = x.Name,
                                SourceMenuItemPath = menuItemPath,
                                DestMenuItemPath = menuItemPath,
                                FilePath = GetFilePath(x),
                                Selected = false
                            };
                        })
                        .Where(x => !string.IsNullOrEmpty(x.SourceMenuItemPath))
                        .OrderByDescending(x => x.SourceMenuItemPath)
                        .ToList();
        }

        private List<EditorWindowFolder> CreateExistFolderList(List<EditorWindowInfo> editorWindowInfoList)
        {
            var dict = new Dictionary<string, EditorWindowFolder>();

            foreach (var editorWindowInfo in editorWindowInfoList)
            {
                var folderName = Regex.Replace(editorWindowInfo.SourceMenuItemPath, "/[^/]+$", string.Empty);

                if (!dict.TryGetValue(folderName, out EditorWindowFolder folder))
                {
                    folder = new EditorWindowFolder()
                    {
                        Name = folderName,
                        ParentFolder = null,
                        NameEdittable = false
                    };
                    dict.Add(folderName, folder);
                }

                folder.EditorWindowList.Add(editorWindowInfo);
            }

            // パスが深いところから処理していく
            var orderedKeys = dict.Keys.OrderByDescending(x => x);

            foreach (var keyName in orderedKeys)
            {
                // ルートフォルダなので親フォルダを探さなくてよい
                if (keyName.IndexOf('/') == -1) continue;

                var parentFolderName = Regex.Replace(keyName, "/[^/]+$", string.Empty);

                // 親フォルダが見つかったのでフォルダ情報を変更する
                if (dict.TryGetValue(parentFolderName, out EditorWindowFolder parentFolder))
                {
                    dict[keyName].Name = keyName.Split('/').Last();
                    dict[keyName].ParentFolder = parentFolder;
                    parentFolder.EditorWindowFolderList.Add(dict[keyName]);
                    dict.Remove(keyName);
                }
            }

            return dict.Values.ToList();
        }

        /// <summary>
        /// パスが除外するフォルダに入ったMenuItemアトリビュートが含まれるか判断する
        /// </summary>
        /// <param name="attrData">アトリビュートのデータ</param>
        /// <param name="exclusionFolderNames">除外するフォルダの一覧</param>
        /// <returns>含まれる場合true</returns>
        private bool ContainExclusionFolder(CustomAttributeData attrData, IReadOnlyCollection<string> exclusionFolderNames)
        {
            foreach (var arg in attrData.ConstructorArguments)
            {
                var path = arg.Value as string;
                if (string.IsNullOrEmpty(path)) continue;

                if (exclusionFolderNames.Any(x => path.StartsWith(x)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// すぐにコンパイルを実行する
        /// </summary>
        private static void ForceCompile()
        {
            EditorApplication.ExecuteMenuItem("Assets/Refresh");
        }

        /// <summary>
        /// MenuItemのパスを変更する
        /// </summary>
        /// <param name="editorWindowInfoList"></param>
        private void ReplaceMenuItem(List<EditorWindowInfo> editorWindowInfoList)
        {
            foreach (var editorWindowInfo in editorWindowInfoList)
            {
                if (!editorWindowInfo.HasChanged) continue;

                var code = File.ReadAllText(editorWindowInfo.FilePath);

                var regexWithReplaced = new Regex(@"(?<part1>\/\/" + Regex.Escape(T00L_KEYWORD) + @"\[MenuItem\(.*\)](\n|\t|\s|\r)*\[MenuItem\("")(?<replaced>.*)(?<part2>"".*\)])");
                var matchWithReplaced = regexWithReplaced.Match(code);

                // 一度でも編集済みかどうか
                if (matchWithReplaced.Success)
                {
                    code = code.Replace(matchWithReplaced.Value,
                            $"{matchWithReplaced.Groups["part1"]}{editorWindowInfo.DestMenuItemPath}{matchWithReplaced.Groups["part2"]}");
                }
                else
                {
                    var match = Regex.Match(code, @"(?<indent>(\t|\s)*)(?<part1>\[MenuItem\("")(?<menuitem>.*)(?<part2>"".*\)])");

                    if (match.Success)
                    {
                        Debug.Log("Sucess:" + match.Value);
                        code = code.Replace(match.Value,
                            $"{match.Groups["indent"]}//{T00L_KEYWORD}{match.Value.Substring(match.Groups["indent"].Length)}" +
                            $"{match.Groups["indent"]}{match.Groups["part1"]}{editorWindowInfo.DestMenuItemPath}{match.Groups["part2"]}");
                    }
                }

                File.WriteAllText(editorWindowInfo.FilePath, code);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            editorWindowInfoList = LoadEditorWindowList();
            folderList = CreateExistFolderList(editorWindowInfoList);
        }

        private string GetMenuItemFolderPath(EditorWindowFolder folder)
        {
            var currentFolder = folder;
            var path = folder.Name;
            while (currentFolder.ParentFolder != null)
            {
                path = folder.ParentFolder.Name + "/" + path;
                currentFolder = folder.ParentFolder;
            }

            return path;
        }
    }
}