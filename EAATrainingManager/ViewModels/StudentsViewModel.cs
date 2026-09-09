using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EAATrainingManager.Models;
using EAATrainingManager.Services;

namespace EAATrainingManager.ViewModels;

public partial class StudentsViewModel : ObservableObject
{
    private readonly DatabaseService _dbService;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedStatusFilter = "الجميع";

    [ObservableProperty]
    private Student? _selectedStudent;

    [ObservableProperty]
    private bool _isTrajectoryOpen;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _filteredCount;

    public ObservableCollection<Student> Students { get; } = new();

    public StudentsViewModel(DatabaseService dbService)
    {
        _dbService = dbService;
    }

    [RelayCommand]
    public async Task LoadStudentsAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _dbService.GetAllStudentsAsync(SearchQuery, SelectedStatusFilter);
            Students.Clear();
            foreach (var s in list)
            {
                Students.Add(s);
            }
            FilteredCount = Students.Count;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        await LoadStudentsAsync();
    }

    [RelayCommand]
    public async Task OpenTrajectoryAsync(Student student)
    {
        if (student == null) return;

        IsLoading = true;
        try
        {
            var detailed = await _dbService.GetStudentWithTrajectoryAsync(student.Id);
            SelectedStudent = detailed ?? student;
            IsTrajectoryOpen = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void CloseTrajectory()
    {
        IsTrajectoryOpen = false;
        SelectedStudent = null;
    }
}
