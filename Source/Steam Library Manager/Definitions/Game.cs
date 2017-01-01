﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Steam_Library_Manager.Definitions
{
    public class Game
    {
        public int AppID { get; set; }
        public string AppName { get; set; }
        public string GameHeaderImage { get; set; }
        public string PrettyGameSize { get; set; }
        public DirectoryInfo InstallationPath, CommonPath, DownloadingPath, WorkShopPath;
        public FileInfo FullAcfPath, WorkShopAcfPath, CompressedArchiveName;
        public string AcfName, WorkShopAcfName;
        public long SizeOnDisk { get; set; }
        public Framework.AsyncObservableCollection<FrameworkElement> ContextMenuItems { get; set; }
        public bool IsCompressed { get; set; }
        public bool IsSteamBackup { get; set; }
        public Library InstalledLibrary { get; set; }

        public Framework.AsyncObservableCollection<FrameworkElement> GenerateRightClickMenuItems()
        {
            Framework.AsyncObservableCollection<FrameworkElement> rightClickMenu = new Framework.AsyncObservableCollection<FrameworkElement>();
            try
            {
                foreach (List.ContextMenu cItem in List.gameContextMenuItems.Where(x => x.IsActive))
                {
                    if (IsSteamBackup && cItem.ShowToSteamBackup == Enums.menuVisibility.NotVisible)
                        continue;
                    else if (InstalledLibrary.Backup && cItem.ShowToSLMBackup == Enums.menuVisibility.NotVisible)
                        continue;
                    else if (IsCompressed && cItem.ShowToCompressed == Enums.menuVisibility.NotVisible)
                        continue;
                    else if (cItem.ShowToNormal == Enums.menuVisibility.NotVisible)
                        continue;

                    if (cItem.IsSeparator)
                        rightClickMenu.Add(new Separator());
                    else
                    {
                        MenuItem slmItem = new MenuItem()
                        {
                            Tag = this,
                            Header = string.Format(cItem.Header, AppName, AppID, Functions.FileSystem.FormatBytes(SizeOnDisk))
                        };
                        slmItem.Tag = cItem.Action;
                        slmItem.Icon = Functions.fAwesome.getAwesomeIcon(cItem.Icon, cItem.IconColor);
                        slmItem.HorizontalContentAlignment = HorizontalAlignment.Left;
                        slmItem.VerticalContentAlignment = VerticalAlignment.Center;

                        rightClickMenu.Add(slmItem);
                    }
                }

                return rightClickMenu;
            }
            catch (FormatException ex)
            {
                MessageBox.Show($"An error happened while parsing context menu, most likely happened duo typo on color name.\n\n{ex}");

                return rightClickMenu;
            }
        }

        public void ParseMenuItemAction(string Action)
        {
            switch (Action.ToLowerInvariant())
            {
                default:
                    if (string.IsNullOrEmpty(SLM.userSteamID64))
                        return;

                    System.Diagnostics.Process.Start(string.Format(Action, AppID, SLM.userSteamID64));
                    break;
                case "disk":
                    if (CommonPath.Exists)
                        System.Diagnostics.Process.Start(CommonPath.FullName);
                    break;
                case "acffile":
                    System.Diagnostics.Process.Start(FullAcfPath.FullName);
                    break;
                case "deletegamefilesslm":

                    DeleteFiles();
                    RemoveFromLibrary();

                    break;
            }
        }

        public List<FileSystemInfo> GetFileList(bool includeDownloads = true, bool includeWorkshop = true)
        {
            List<FileSystemInfo> FileList = new List<FileSystemInfo>();

            if (IsCompressed)
            {
                FileList.Add(CompressedArchiveName);
            }
            else
            {
                if (CommonPath.Exists)
                {
                    FileList.AddRange(GetCommonFiles());
                }

                if (includeDownloads && DownloadingPath.Exists)
                {
                    FileList.AddRange(GetDownloadFiles());
                    FileList.AddRange(GetPatchFiles());
                }

                if (includeWorkshop && WorkShopPath.Exists)
                {
                    FileList.AddRange(GetWorkshopFiles());
                }
            }
            return FileList;
        }

        public List<FileSystemInfo> GetCommonFiles()
        {
            return CommonPath.GetFileSystemInfos("*", SearchOption.AllDirectories).Where(x => !x.Attributes.HasFlag(FileAttributes.Directory)).ToList();
        }

        public List<FileSystemInfo> GetDownloadFiles()
        {
            return DownloadingPath.GetFileSystemInfos("*", SearchOption.AllDirectories).Where(x => !x.Attributes.HasFlag(FileAttributes.Directory)).ToList();
        }

        public List<FileSystemInfo> GetPatchFiles()
        {
            return InstalledLibrary.downloadPath.GetFileSystemInfos("*", SearchOption.AllDirectories).Where(x => !x.Attributes.HasFlag(FileAttributes.Directory)).ToList();
        }

        public List<FileSystemInfo> GetWorkshopFiles()
        {
            return WorkShopPath.GetFileSystemInfos("*", SearchOption.AllDirectories).Where(x => !x.Attributes.HasFlag(FileAttributes.Directory)).ToList();
        }

        public void CopyGameFiles(Forms.MoveGameForm currentForm, Library targetLibrary, CancellationTokenSource cancellationToken, bool compressGame = false)
        {
            currentForm.formLogs.Add($"Populating file list, please wait");

            ConcurrentBag<string> copiedFiles = new ConcurrentBag<string>();
            ConcurrentBag<string> createdDirectories = new ConcurrentBag<string>();
            List<FileSystemInfo> gameFiles = GetFileList();
            System.Diagnostics.Stopwatch timeElapsed = new System.Diagnostics.Stopwatch();
            timeElapsed.Start();

            try
            {
                long totalFileSize = 0;
                ParallelOptions parallelOptions = new ParallelOptions()
                {
                    CancellationToken = cancellationToken.Token
                };

                Parallel.ForEach(gameFiles, parallelOptions, file =>
                {
                    Interlocked.Add(ref totalFileSize, (file as FileInfo).Length);
                });

                currentForm.formLogs.Add($"File list populated, total files to move: {gameFiles.Count}");

                if (!IsCompressed && compressGame)
                {
                    FileInfo compressedArchive = new FileInfo(CompressedArchiveName.FullName.Replace(InstalledLibrary.steamAppsPath.FullName, targetLibrary.steamAppsPath.FullName));

                    if (compressedArchive.Exists)
                        compressedArchive.Delete();

                    using (ZipArchive compressed = ZipFile.Open(compressedArchive.FullName, ZipArchiveMode.Create))
                    {
                        copiedFiles.Add(compressedArchive.FullName);

                        foreach (FileSystemInfo currentFile in gameFiles)
                        {
                            string newFileName = currentFile.FullName.Substring(InstalledLibrary.steamAppsPath.FullName.Length + 1);

                            compressed.CreateEntryFromFile(currentFile.FullName, newFileName, CompressionLevel.Optimal);

                            currentForm.ReportFileMovement(newFileName, gameFiles.Count, (currentFile as FileInfo).Length, totalFileSize);

                            if (cancellationToken.IsCancellationRequested)
                                throw new OperationCanceledException(cancellationToken.Token);
                        }

                        compressed.CreateEntryFromFile(FullAcfPath.FullName, AcfName);
                    }
                }
                else if (IsCompressed && !compressGame)
                {
                    foreach (ZipArchiveEntry currentFile in ZipFile.OpenRead(CompressedArchiveName.FullName).Entries)
                    {
                        FileInfo newFile = new FileInfo(Path.Combine(targetLibrary.steamAppsPath.FullName, currentFile.FullName));

                        if (!newFile.Directory.Exists)
                        {
                            newFile.Directory.Create();
                            createdDirectories.Add(newFile.Directory.FullName);
                        }

                        currentFile.ExtractToFile(newFile.FullName, true);
                        copiedFiles.Add(newFile.FullName);

                        currentForm.ReportFileMovement(newFile.FullName, gameFiles.Count, currentFile.Length, totalFileSize);

                        if (cancellationToken.IsCancellationRequested)
                            throw new OperationCanceledException(cancellationToken.Token);
                    }
                }
                else
                {
                    Parallel.ForEach(gameFiles.Where(x => (x as FileInfo).Length <= Properties.Settings.Default.ParallelAfterSize), parallelOptions, currentFile =>
                    {
                        FileInfo newFile = new FileInfo(currentFile.FullName.Replace(InstalledLibrary.steamAppsPath.FullName, targetLibrary.steamAppsPath.FullName));

                        if (!newFile.Exists || (newFile.Length != (currentFile as FileInfo).Length || newFile.LastWriteTime != (currentFile as FileInfo).LastWriteTime))
                        {
                            if (!newFile.Directory.Exists)
                            {
                                newFile.Directory.Create();
                                createdDirectories.Add(newFile.Directory.FullName);
                            }

                            (currentFile as FileInfo).CopyTo(newFile.FullName, true);
                        }

                        copiedFiles.Add(newFile.FullName);

                        currentForm.ReportFileMovement(newFile.FullName, gameFiles.Count, (currentFile as FileInfo).Length, totalFileSize);
                    });

                    parallelOptions.MaxDegreeOfParallelism = 1;

                    Parallel.ForEach(gameFiles.Where(x => (x as FileInfo).Length > Properties.Settings.Default.ParallelAfterSize), parallelOptions, currentFile =>
                    {
                        FileInfo newFile = new FileInfo(currentFile.FullName.Replace(InstalledLibrary.steamAppsPath.FullName, targetLibrary.steamAppsPath.FullName));

                        if (!newFile.Exists || (newFile.Length != (currentFile as FileInfo).Length || newFile.LastWriteTime != (currentFile as FileInfo).LastWriteTime))
                        {
                            if (!newFile.Directory.Exists)
                            {
                                newFile.Directory.Create();
                                createdDirectories.Add(newFile.Directory.FullName);
                            }

                            (currentFile as FileInfo).CopyTo(newFile.FullName, true);
                        }
                        copiedFiles.Add(newFile.FullName);

                        currentForm.ReportFileMovement(newFile.FullName, gameFiles.Count, (currentFile as FileInfo).Length, totalFileSize);
                    });

                    if (!IsCompressed)
                    {
                        // Copy .ACF file
                        if (FullAcfPath.Exists)
                        {
                            FullAcfPath.CopyTo(Path.Combine(targetLibrary.steamAppsPath.FullName, AcfName), true);
                            currentForm.ReportFileMovement(Path.Combine(targetLibrary.steamAppsPath.FullName, AcfName), gameFiles.Count, FullAcfPath.Length, totalFileSize);
                        }

                        if (WorkShopAcfPath.Exists)
                        {
                            FileInfo newACFPath = new FileInfo(WorkShopAcfPath.FullName.Replace(InstalledLibrary.steamAppsPath.FullName, targetLibrary.steamAppsPath.FullName));

                            if (!newACFPath.Directory.Exists)
                            {
                                newACFPath.Directory.Create();
                                createdDirectories.Add(newACFPath.Directory.FullName);
                            }

                            WorkShopAcfPath.CopyTo(newACFPath.FullName, true);
                            currentForm.ReportFileMovement(newACFPath.FullName, gameFiles.Count, newACFPath.Length, totalFileSize);
                        }
                    }
                }

                // TODO
                if (Properties.Settings.Default.PlayASoundOnCompletion)
                {
                    System.Media.SystemSounds.Exclamation.Play();
                }

                timeElapsed.Stop();
                currentForm.formLogs.Add($"Time elapsed: {timeElapsed.Elapsed}");
                Application.Current.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, (Action)delegate
                {
                    currentForm.button.Content = "Close";
                });
            }
            catch (OperationCanceledException)
            {
                MessageBoxResult removeMovenFiles = MessageBox.Show("Game movement cancelled. Would you like to remove files that already moven?", "Remove moven files?", MessageBoxButton.YesNo);

                if (removeMovenFiles == MessageBoxResult.Yes)
                    Functions.FileSystem.RemoveGivenFiles(copiedFiles, createdDirectories);

                currentForm.formLogs.Add($"Operation cancelled by user. Time Elapsed: {timeElapsed.Elapsed}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                MessageBoxResult removeMovenFiles = MessageBox.Show("An error happened while moving game files. Would you like to remove files that already moven?", "Remove moven files?", MessageBoxButton.YesNo);

                if (removeMovenFiles == MessageBoxResult.Yes)
                    Functions.FileSystem.RemoveGivenFiles(copiedFiles, createdDirectories);

                currentForm.formLogs.Add($"An error happened while moving game files. Time Elapsed: {timeElapsed.Elapsed}");
            }
        }

        public bool DeleteFiles()
        {
            try
            {
                if (IsCompressed)
                {
                    CompressedArchiveName.Delete();
                }
                else if (IsSteamBackup)
                {
                    if (InstallationPath.Exists)
                        InstallationPath.Delete(true);
                }
                else
                {
                    List<FileSystemInfo> gameFiles = GetFileList();

                    Parallel.ForEach(gameFiles, currentFile =>
                    {
                        if (currentFile.Exists)
                            currentFile.Delete();
                    }
                    );

                    // common folder, if exists
                    if (CommonPath.Exists)
                        CommonPath.Delete(true);

                    // downloading folder, if exists
                    if (DownloadingPath.Exists)
                        DownloadingPath.Delete(true);

                    // workshop folder, if exists
                    if (WorkShopPath.Exists)
                        WorkShopPath.Delete(true);

                    // game .acf file
                    if (FullAcfPath.Exists)
                        FullAcfPath.Delete();

                    // workshop .acf file
                    if (WorkShopAcfPath.Exists)
                        WorkShopAcfPath.Delete();
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());

                return false;
            }
        }

        public void RemoveFromLibrary()
        {
            InstalledLibrary.Games.Remove(this);

            InstalledLibrary.UpdateLibraryVisual();

            if (SLM.selectedLibrary == InstalledLibrary)
                Functions.Games.UpdateMainForm(InstalledLibrary);
        }
    }
}