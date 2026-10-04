namespace FileViz.App.ViewModels;
public sealed class DuplicateScopeRow(long snapshot,string root,bool selected) : Bindable
{
    public long Snapshot { get; }=snapshot;public string Root { get; }=root;
    private bool chosen=selected;public bool Selected{get=>chosen;set=>Set(ref chosen,value);}
}