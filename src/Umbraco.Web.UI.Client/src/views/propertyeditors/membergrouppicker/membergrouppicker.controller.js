//this controller simply tells the dialogs service to open a memberPicker window
//with a specified callback, this callback will receive an object with a selection on it
function memberGroupPicker($scope, editorService, memberGroupResource){

    var vm = this;

    vm.openMemberGroupPicker = openMemberGroupPicker;
    vm.remove = remove;
    vm.clear = clear;

    function trim(str, chr) {
        var rgxtrim = (!chr) ? new RegExp('^\\s+|\\s+$', 'g') : new RegExp('^' + chr + '+|' + chr + '+$', 'g');
        return str.replace(rgxtrim, '');
    }

    $scope.renderModel = [];
    $scope.allowRemove = true;
    $scope.groupIds = [];

    if ($scope.model.value) {
        var groupIds = $scope.model.value.split(',');
        memberGroupResource.getByIds(groupIds).then(function(groups) {
            $scope.renderModel = groups;
        });
    }

    function setDirty() {
        if ($scope.modelValueForm) {
            $scope.modelValueForm.modelValue.$setDirty();
        }
    }

    function openMemberGroupPicker() {
        var memberGroupPicker = {
            multiPicker: true,
            submit: function (model) {
                var selectedGroupIds = _.map(model.selectedMemberGroups
                    ? model.selectedMemberGroups
                    : [model.selectedMemberGroup],
                    function (id) { return parseInt(id); }
                );
                // figure out which groups are new and fetch them
                var newGroupIds = _.difference(selectedGroupIds, renderModelIds());
                if (newGroupIds && newGroupIds.length) {
                    memberGroupResource.getByIds(newGroupIds).then(function (groups) {
                        $scope.renderModel = _.union($scope.renderModel, groups);
                        setDirty();
                        editorService.close();
                    });
                }
                else {
                    // no new groups selected
                    editorService.close();
                }
            },
            close: function() {
                editorService.close();
            }
        };
        editorService.memberGroupPicker(memberGroupPicker);
    }

    function remove(index) {
        $scope.renderModel.splice(index, 1);
        setDirty();
    }

    function clear() {
        $scope.renderModel = [];
        setDirty();
    }

    function renderModelIds() {
        return _.map($scope.renderModel, function (i) {
            return i.id;
        });
    }

    var unsubscribe = $scope.$on("formSubmitting", function (ev, args) {
        $scope.model.value = trim(renderModelIds().join(), ",");
    });

    //when the scope is destroyed we need to unsubscribe
    $scope.$on('$destroy', function () {
        unsubscribe();
    });

}

angular.module('umbraco').controller("Umbraco.PropertyEditors.MemberGroupPickerController", memberGroupPicker);
